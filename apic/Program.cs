using System;

using System.IO;
using Tommy;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Net;
using Newtonsoft.Json;
using WatsonWebserver;
using Serilog;




namespace apic
{
    internal static class Program
    {

        public static void Main()
        {
            try
            {
                Console.WriteLine("Apic Gateway");
                ApicClass a = new ApicClass();
                a.Gateway();
            }
            catch (Exception e)
            {
                Console.WriteLine("Fatal error: " + e);
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }
        }
    }




    class ApicClass
    {

        public int Request_counter { get; set; }
        public string Ipaddress { get; set; }
        public string Url { get; set; }
        public string Host { get; set; }
        public string Config_file { get; set; }
        public string Quickbooks_file { get; set; }
        public dynamic Rp { get; set; }
        public TomlTable Config { get; set; }

        private const int QbFileOpenMultiUser = 2;

        private static T RunOnSta<T>(Func<T> work)
        {
            T result = default;
            Exception error = null;
            Thread t = new Thread(() =>
            {
                Log.Information("RunOnSta: worker thread started (STA)");
                try
                {
                    result = work();
                    Log.Information("RunOnSta: worker thread finished");
                }
                catch (Exception e)
                {
                    error = e;
                    Log.Information("RunOnSta: worker thread threw " + e.ToString());
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw error;
            return result;
        }

        private static dynamic CreateRequestProcessor()
        {
            Log.Information("CreateRequestProcessor: trying QBXMLRP2.RequestProcessor");
            Type t = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor");
            if (t == null)
            {
                Log.Information("CreateRequestProcessor: not found, trying QBXMLRP2.RequestProcessor.1");
                t = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor.1");
            }
            if (t == null)
            {
                Log.Information("CreateRequestProcessor: QBXMLRP2 not registered in this machine's COM catalog");
                throw new System.Runtime.InteropServices.COMException(
                    "QuickBooks request processor (QBXMLRP2) is not registered. Install QuickBooks Desktop on this machine.");
            }
            Log.Information("CreateRequestProcessor: resolved " + t.FullName);
            dynamic rp = Activator.CreateInstance(t);
            Log.Information("CreateRequestProcessor: activation OK (" + rp.GetType().FullName + ")");
            return rp;
        }


        public ApicClass()
        {
            Config = Get_config();
            Log.Logger = new LoggerConfiguration()
                              .MinimumLevel.Debug()
                            .WriteTo.Console(

                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                              .CreateLogger();
            Host = Config["host"] == "" ? Dns.GetHostName() : "127.0.0.1";
            Ipaddress = Config["ipaddress"] == "" ? Get_IP4() : "127.0.0.1";
            Url = "http://" + Ipaddress + ":" + Config["port"] + "/";
            Rp = null;

            Log.Information("Config: port=" + Config["port"] + " ipaddress=" + Config["ipaddress"] + " host=" + Config["host"] + " auto_ipaddress=" + Config["auto_ipaddress"]);
            Log.Information("Resolved: Ipaddress=" + Ipaddress + " Host=" + Host + " Url=" + Url);
            Log.Information("Quickbooks file (raw): " + Config["quickbooks_file"]);


        }
        public void Gateway()
        {

            string logo = @"
	             _____    _____        
	     /\     |  __ \  |_   _|       
	   / /\ \   |  ___/    | |    / __|
	  / ____ \  | |       _| |_  | (__ 
	 /_/    \_\ |_|      |_____|  \___|";

            Log.Information(logo);

            Log.Information("Config file: " + Config_file);
            Log.Information("Quickbooks file: " + Config["quickbooks_file"]);
            Log.Information("My IP Address is: " + Ipaddress);
            Log.Information("I'm listening on port " + Config["port"] + "...");
            Log.Information("Access me on: ");
            Log.Information(Url);

            Server server = new Server(Ipaddress, Config["port"], false, Default_route);
            server.Routes.Static.Add(WatsonWebserver.HttpMethod.GET, "/test/", Test_route);
            server.Routes.Static.Add(WatsonWebserver.HttpMethod.POST, "/", Post);
            Log.Information("Routes registered: GET /test/, POST /, GET /* (default)");

            server.Start();
            Log.Information("Server started on " + Ipaddress + ":" + Config["port"]);
            Console.ReadLine();



        }


 

        async Task Post(HttpContext ctx)
        {

            ctx.Response.Headers["Content-Type"] = "application/json";
            Log.Information("Visitor: " + ctx.Request.Useragent);
            Log.Information("Request Enterpoint: /");

            bool ok = true;
            IDictionary<string, string> data = new Dictionary<string, string>();
            Log.Information("POST Request");
            Log.Information("POST ContentType: " + ctx.Request.ContentType);

            string xml = ctx.Request.DataAsString;
            Log.Information("POST body length: " + (xml == null ? 0 : xml.Length) + " chars");

            // Console.WriteLine(xml);


            string[] a = xml.Split(new[] { "<br>" }, StringSplitOptions.None);


            List<string> xmls = new List<string>(a);
            Log.Information("POST split into " + xmls.Count + " chunk(s)");


            foreach (var i in xmls)
            {
                // Console.WriteLine("xxxxxxx");
                // Console.WriteLine(i);
                // Console.WriteLine("xxxxxxx");
                bool x = Validate_xml(i);
                Log.Information("Chunk #" + xmls.IndexOf(i) + " valid: " + x + " (" + (i == null ? 0 : i.Length) + " chars)");

                if (x == false)
                {
                    ok = false;
                }

            }

            if (!ok)
            {
                Log.Information("POST batch rejected - one or more chunks failed validation, QuickBooks NOT called");
            }


            if (ok)
            {
                Log.Information("Complete Batch XML is ok, sending " + xmls.Count + " request(s) to quickbooks");
                List<string> rxml = Send_xmls(xmls);
                Log.Information("POST: QuickBooks answered with " + rxml.Count + " response(s)");
                data["responses"] = Parse_batch_respond(rxml);

            }

            ctx.Response.Headers["Content-Type"] = "application/json";

            string json = JsonConvert.SerializeObject(data);
            Log.Information("POST response: " + json.Length + " chars");

            await ctx.Response.Send(json);
        }

        async Task Test_route(HttpContext ctx)
        {
            IDictionary<string, string> data = new Dictionary<string, string>();

            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Content-Type"] = "application/json";
            Log.Information("Visitor: " + ctx.Request.Useragent);
            Log.Information("Request: /test/");
            bool test = Test_qb_access();
            Log.Information("Test result: " + test);

            data["testing quickbooks file access"] = test.ToString();
            data["quickbooks_file"] = Config["quickbooks_file"];
            string json = JsonConvert.SerializeObject(data);

            await ctx.Response.Send(json);
        }

        async Task Default_route(HttpContext ctx)
        {
            try
            {
            Request_counter += 1;

            string data = Set_json_msg();
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Content-Type"] = "application/json";

            Log.Information("Visitor: " + ctx.Request.Useragent);
            string xml = "";
            var keys = ctx.Request.Query.Elements.Keys;
            Log.Information("Query keys: " + string.Join(", ", keys));
            foreach (var i in keys)
            {
                //Console.WriteLine(i);

                if ((string)i == "xml")
                {
                    string _xml = ctx.Request.Query.Elements["xml"];
                    xml = WebUtility.UrlDecode(_xml);

                    break;
                }

                else if ((string)i == "base64")
                {
                    Log.Information("Query contains base64 payload, decoding");
                    string base64 = ctx.Request.Query.Elements["base64"];
                    base64 = WebUtility.UrlDecode(base64);

                    var base64EncodedBytes = System.Convert.FromBase64String(base64);
                    xml = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
                    break;

                }
            }

            //Console.WriteLine("...check");

            if (xml != "")
            {
                Log.Information("Has a XML Parameter String, xml length: " + xml.Length);

                bool ok = Validate_xml(xml);
                if (ok)
                {
                    Log.Information("XML is ok and sending it to Quickbooks");

                    while (Rp != null)
                    {
                        Log.Information("Quickbooks file is busy, please wait\t" + Request_counter);
                        await Task.Delay(2000);
                    }

                    string rxml = Send_xml(xml);
                    Log.Information("Quickbooks responded, sending response to parase it\t" + Request_counter);
                    data = ParseRespond(rxml);
                }
                else
                {
                    data = Set_json_msg("XML is not correctly formatted");
                }
            }
            Log.Information("Deliverd JSON Data \t" + Request_counter + " (" + (data == null ? 0 : data.Length) + " chars)");
            await ctx.Response.Send(data);
            }
            catch (Exception ex)
            {
                Log.Information("Default_route failed: " + ex.ToString());
                ctx.Response.StatusCode = 200;
                ctx.Response.Headers["Content-Type"] = "application/json";
                await ctx.Response.Send(Set_json_msg("Error: " + ex.Message));
            }
        }

        public static bool Validate_xml(string xml)
        {
            bool r = false;
            XmlDocument doc = new XmlDocument();
            try
            {
                doc.LoadXml(xml);
                r = true;
            }
            catch (Exception e)
            {
                Log.Information($"Error: {e}");
                Log.Information(xml.Length > 300 ? xml.Substring(0, 300) + "..." : xml);
                r = false;
            }
            return r;
        }


        public List<string> Send_xmls(List<string> xmls)
        {
            List<string> responses = new List<string>();
            try
            {
                return RunOnSta(() => Send_xmls_impl(xmls));
            }
            catch (Exception ex)
            {
                Log.Information("Error: " + ex.ToString());
                responses.Add("COM Error Description = " + ex.Message + "COM error");
                return responses;
            }
        }

        public List<string> Send_xmls_impl(List<string> xmls)
        {
            string qbfile = Config["quickbooks_file"].ToString();
            string session = null;
            string response = null;

            List<string> responses = new List<string>();

            Log.Information("Send_xmls_impl: entering, " + xmls.Count + " request(s), company file: " + qbfile);
            try
            {
                Rp = CreateRequestProcessor();
                Log.Information("Send_xmls_impl: calling OpenConnection(\"APIc\", \"APIc\")");
                Rp.OpenConnection("APIc", "APIc");
                Log.Information("Send_xmls_impl: OpenConnection returned");
                Log.Information("Send_xmls_impl: calling BeginSession (mode " + QbFileOpenMultiUser + " = multi-user)");
                session = Rp.BeginSession(qbfile, QbFileOpenMultiUser);
                Log.Information("Send_xmls_impl: BeginSession OK, ticket: " + session);

                int c = xmls.Count;
                foreach (var i in xmls)
                {
                    Log.Information("process no:" + c + ", payload length: " + (i == null ? 0 : i.Length));

                    response = Rp.ProcessRequest(session, i);
                    Log.Information("ProcessRequest response: " + (response == null ? 0 : response.Length) + " chars");
                    responses.Add(response);
                    c--;
                }

                System.Threading.Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                string error = "COM Error Description = " + ex.Message + "COM error";
                Console.WriteLine(error);
                Log.Information("Send_xmls_impl failed: " + ex.ToString());
                responses.Add(error);

            }
            finally
            {
                Log.Information("...closing");
                if (session != null)
                {
                    Log.Information("...close session");
                    Rp.EndSession(session);
                }
                if (Rp != null)
                {
                    Log.Information("...close connection");
                    Rp.CloseConnection();
                    Rp = null;
                    System.Threading.Thread.Sleep(2000);
                }
            }
            return responses;
        }



        public string Send_xml(String xml_str)
        {
            try
            {
                return RunOnSta(() => Send_xml_impl(xml_str));
            }
            catch (Exception ex)
            {
                Log.Information(ex.ToString());
                return "COM Error Description = " + ex.Message + "COM error";
            }
        }

        public string Send_xml_impl(String xml_str)
        {
            string qbfile = Config["quickbooks_file"].ToString();
            string ticket = null;
            string response = null;

            List<string> responses = new List<string>();
            Log.Information("Send_xml_impl: entering, payload length: " + (xml_str == null ? 0 : xml_str.Length) + ", company file: " + qbfile);
            try
            {
                Rp = CreateRequestProcessor();
                Log.Information("Send_xml_impl: calling OpenConnection(\"APIc\", \"APIc\")");
                Rp.OpenConnection("APIc", "APIc");
                Log.Information("Send_xml_impl: OpenConnection returned");
                Log.Information("Send_xml_impl: calling BeginSession (mode " + QbFileOpenMultiUser + " = multi-user)");
                ticket = Rp.BeginSession(qbfile, QbFileOpenMultiUser);
                Log.Information("Send_xml_impl: BeginSession OK, ticket: " + ticket);
                Log.Information("Send_xml_impl: calling ProcessRequest");
                response = Rp.ProcessRequest(ticket, xml_str);
                Log.Information("Send_xml_impl: ProcessRequest returned (" + (response == null ? 0 : response.Length) + " chars)");
                System.Threading.Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Log.Information("Send_xml_impl failed: " + ex.ToString());
                return "COM Error Description = " + ex.Message + "COM error";

            }
            finally
            {
                if (ticket != null)
                {
                    Rp.EndSession(ticket);
                }
                if (Rp != null)
                {
                    Rp.CloseConnection();
                    Rp = null;
                    System.Threading.Thread.Sleep(2000);
                }
            }
            return response;
        }


        public string Parse_batch_respond(List<String> response)
        {

            List<string> data = new List<string>();
            String res = "".ToString();
            Log.Information("Parse_batch_respond: parsing " + response.Count + " response(s)");
            foreach (var r in response)
            {
                //Console.WriteLine(r);
                bool is_ok = Validate_xml(r);
                Log.Information("Parse_batch_respond: response valid XML: " + is_ok + " (" + (r == null ? 0 : r.Length) + " chars)");
                if (is_ok)
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(r);
                    var json = JsonConvert.SerializeXmlNode(doc);
                    data.Add(json);

                }
                else
                {
                    data.Add(r);

                }

            }

            return JsonConvert.SerializeObject(data);
        }

        public string ParseRespond(String response)
        {
            if (response == null)
            {
                Log.Information("ParseRespond: QuickBooks returned null response");
                return Set_json_msg("No response from QuickBooks");
            }
            bool valid_xml = Validate_xml(response);
            Log.Information("ParseRespond: valid XML: " + valid_xml + " (" + (response == null ? 0 : response.Length) + " chars)");

            if (valid_xml)
            {
                string str = response.Replace("<QBXML>", "");
                str = str.Replace("</QBXML>", "");
                str = str.Replace("<QBXMLMsgsRs>", "");
                str = str.Replace("</QBXMLMsgsRs>", "");

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(str);

                foreach (XmlNode node in doc)
                {
                    if (node.NodeType == XmlNodeType.XmlDeclaration)
                    {
                        doc.RemoveChild(node);
                    }
                }
                return JsonConvert.SerializeXmlNode(doc);
            }
            else
            {
                return Set_json_msg("Not a valid response");
            }


        }




        public Boolean Test_qb_access()
        {
            return RunOnSta(Test_qb_access_impl);
        }

        public Boolean Test_qb_access_impl()
        {
            bool r = false;
            string qbfile = Config["quickbooks_file"].ToString();
            Log.Information("Test_qb_access_impl: entering");
            Log.Information("Check if file exists: ");
            Log.Information(qbfile);
            if (!File.Exists(@Config["quickbooks_file"]))
            {
                Log.Information("File NOT found: " + Config["quickbooks_file"]);
            }
            if (File.Exists(@Config["quickbooks_file"]))
            {
                Log.Information("File OK");
                if (Rp == null)
                {
                    Log.Information("Rp is null");
                    string ticket = null;



                    try
                    {
                        Log.Information("...request");
                        Log.Information("Test_qb_access_impl: creating request processor");
                        Rp = CreateRequestProcessor();
                        Log.Information("Test_qb_access_impl: calling OpenConnection");
                        Rp.OpenConnection("APIc", "APIc");
                        Log.Information("Test_qb_access_impl: OpenConnection returned");
                        Log.Information("Access your company file - Testing!");
                        Log.Information("Test_qb_access_impl: calling BeginSession (mode " + QbFileOpenMultiUser + " = multi-user)");
                        ticket = Rp.BeginSession(qbfile, QbFileOpenMultiUser);
                        Log.Information("Test_qb_access_impl: BeginSession OK, ticket: " + ticket);
                        Log.Information("Access granted");
                        r = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Information("Error");
                        Log.Information(ex.ToString());
                    }
                    finally
                    {
                        if (ticket != null)
                        {
                            Log.Information("Test_qb_access_impl: EndSession");
                            Rp.EndSession(ticket);
                            Rp = null;
                        }
                        if (Rp != null)
                        {
                            Log.Information("Test_qb_access_impl: CloseConnection");
                            Rp.CloseConnection();
                            Rp = null;
                        }
                    }



                    //r = true;


                }
            }

            return r;
        }

 	        public String Set_json_msg(string msg = "")
	        {
	            IDictionary<string, string> a = new Dictionary<string, string>();
	            if (msg != "")
	            {
	                a.Add("msg", msg);
	            }
	            else
	            {
	                a.Add("msg", "Welcome to Appic Server...");
	                a.Add("test quickbooks file", Url+ "/test");
	      }
	
	            string json = JsonConvert.SerializeObject(a);
	            return json;
	        }

        public string Get_IP4()
        {
            string ip = "127.0.0.1";
            string host = Dns.GetHostName();
            foreach (var i in Dns.GetHostEntry(host).AddressList)
            {
                if (i.AddressFamily.ToString() == "InterNetwork")
                {
                    return i.ToString();
                }
            }
            return ip;
        }


        public TomlTable Get_config()
        {

            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDoc‌​uments), "", "apic_config.toml");
            Config_file = path;
            Console.WriteLine("...config path: " + path);
            if (!File.Exists(path))
            {
                Console.WriteLine("...config missing, creating default at " + path);
                Console.WriteLine("file does not exists");
                Log.Information("configuration.toml does not exists, create a new one");
                TomlTable toml = new TomlTable
                {
                    ["title"] = "APIc",

                    ["quickbooks_file"] = "",
                    ["port"] = 8000,
                    ["ipaddress"] = "",
                    ["auto_ipaddress"] = true,
                    ["logfile"] = "logs/apic_server.txt",
                    ["host"] = "",


                };
                using (StreamWriter writer = File.CreateText(path))
                {
                    toml.WriteTo(writer);
                    writer.Flush();
                }

            }

            else
            {
                Console.WriteLine("...config exists, loading");
            }

            using (StreamReader reader = File.OpenText(path))
            {
                try
                {
                    TomlTable config = TOML.Parse(reader);
                    Console.WriteLine("...config parsed OK, quickbooks_file=" + config["quickbooks_file"]);
                    return config;
                }
                catch (Exception e)
                {
                    Console.WriteLine("...config parse FAILED: " + e);
                    throw;
                }
            }
        }


    }
}


