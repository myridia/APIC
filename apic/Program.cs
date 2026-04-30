using System;

using System.ServiceProcess;
using System.IO;
using Tommy;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using System.Net;
using Newtonsoft.Json;
using WatsonWebserver;
using System.ComponentModel;
using Interop.QBXMLRP2Lib;
using Serilog;




namespace apic
{
    internal static class Program
    {

        public static void Main()
        {
            Console.WriteLine("Apic Gateway");
            ApicClass a = new ApicClass();
            a.Gateway();


        }
    }




    class ApicClass
    {

        [DefaultValue("")]
        public int Request_counter { get; set; }
        public string Ipaddress { get; set; }
        public string Url { get; set; }
        public string Host { get; set; }
        public string Config_file { get; set; }
        public string Quickbooks_file { get; set; }
        public RequestProcessor2 Rp { get; set; }
        public TomlTable Config { get; set; }


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

            server.Start();
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
            Log.Information(ctx.Request.ContentType);

            string xml = ctx.Request.DataAsString;

            // Console.WriteLine(xml);


            string[] a = xml.Split(new[] { "<br>" }, StringSplitOptions.None);


            List<string> xmls = new List<string>(a);


            foreach (var i in xmls)
            {
                // Console.WriteLine("xxxxxxx");
                // Console.WriteLine(i);
                // Console.WriteLine("xxxxxxx");
                bool x = Validate_xml(i);

                if (x == false)
                {
                    ok = false;
                }

            }


            if (ok)
            {
                Log.Information("Complete Batch XML is ok, sending them to quickbooks");
                List<string> rxml = Send_xmls(xmls);
                data["responses"] = Parse_batch_respond(rxml);

            }

            ctx.Response.Headers["Content-Type"] = "application/json";

            string json = JsonConvert.SerializeObject(data);

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

            data["testing quickbooks file access"] = test.ToString();
            data["quickbooks_file"] = Config["quickbooks_file"];
            string json = JsonConvert.SerializeObject(data);

            await ctx.Response.Send(json);
        }

        async Task Default_route(HttpContext ctx)
        {
            Request_counter += 1;

            string data = Set_json_msg();
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Content-Type"] = "application/json";

            Log.Information("Visitor: " + ctx.Request.Useragent);
            string xml = "";
            var keys = ctx.Request.Query.Elements.Keys;
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
                    Console.WriteLine("...has base64");
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
                Log.Information("Has a XML Parameter String...");

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
            Log.Information("Deliverd JSON Data \t" + Request_counter);
            await ctx.Response.Send(data);
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
            catch (XmlException e)
            {
                Log.Information($"Error: {e}");
                Log.Information(xml);
                r = false;
            }
            return r;
        }


        public List<string> Send_xmls(List<string> xmls)
        {
            string qbfile = Config["quickbooks_file"].ToString();
            string session = null;
            string response = null;

            List<string> responses = new List<string>();

            try
            {
                Rp = new RequestProcessor2();
                Rp.OpenConnection("APIc", "APIc");
                session = Rp.BeginSession(qbfile, QBFileMode.qbFileOpenMultiUser);

                int c = xmls.Count;
                foreach (var i in xmls)
                {
                    Log.Information("process no:" + c);

                    response = Rp.ProcessRequest(session, i);
                    responses.Add(response);
                    c--;
                }

                System.Threading.Thread.Sleep(2000);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                string error = "COM Error Description = " + ex.Message + "COM error";
                Console.WriteLine(error);
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
            string qbfile = Config["quickbooks_file"].ToString();
            string ticket = null;
            string response = null;

            List<string> responses = new List<string>();
            try
            {
                Rp = new RequestProcessor2();
                Rp.OpenConnection("APIc", "APIc");
                ticket = Rp.BeginSession(qbfile, QBFileMode.qbFileOpenMultiUser);
                response = Rp.ProcessRequest(ticket, xml_str);
                System.Threading.Thread.Sleep(2000);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
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
            foreach (var r in response)
            {
                //Console.WriteLine(r);
                bool is_ok = Validate_xml(r);
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
            bool valid_xml = Validate_xml(response);

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
            bool r = false;
            //string qbfile = @"Q:\New QB File\shining light body jewelry - working.qbw";
            string qbfile = Config["quickbooks_file"].ToString();
            Log.Information("Check if file exists: ");
            Log.Information(qbfile);
            if (File.Exists(@Config["quickbooks_file"]))
            {
                Log.Information("File OK");
                if (Rp == null)
                {
                    Log.Information("Rp is null");
                    string ticket = null;



                    try
                    {
                        Log.Information("...requestl");
                        Rp = new RequestProcessor2();
                        Rp.OpenConnection("APIc", "APIc");
                        Log.Information("Access your company file - Testing!");
                        Log.Information(qbfile);
                        ticket = Rp.BeginSession(qbfile, QBFileMode.qbFileOpenMultiUser);
                        r = true;
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        Log.Information("Error");
                        Log.Information(ex.ToString());
                    }
                    finally
                    {
                        if (ticket != null)
                        {
                            Rp.EndSession(ticket);
                            Rp = null;
                        }
                        if (Rp != null)
                        {
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
            //Console.WriteLine(path);
            // System.IO.Directory.CreateDirectory(path);


            //string path = "configuration.toml";
            if (!File.Exists(path))
            {
                Console.WriteLine("file does not exists");
                Log.Information("configuration.toml does not exists, create a new one");
                TomlTable toml = new TomlTable
                {
                    ["title"] = "APIc",

                    ["quickbooks_file"] = "Q:\\New QB File\\shining light body jewelry - working.qbw",
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

            using (StreamReader reader = File.OpenText(path))
            {
                TomlTable config = TOML.Parse(reader);
                return config;
            }
        }


    }

    internal class HttpContextBase
    {
        public object Response { get; internal set; }
    }
}


