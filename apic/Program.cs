using System;
using System.IO;
using Tommy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

        public static async Task Main(string[] args)
        {
            try
            {
                bool service = false;
                foreach (var arg in args)
                {
                    if (arg == "--service" || arg == "-s")
                    {
                        service = true;
                    }
                }

                if (service)
                {
                    await Host.CreateDefaultBuilder(args)
                        .UseWindowsService(o => o.ServiceName = "APIC")
                        .ConfigureServices(s =>
                        {
                            s.AddSingleton<ApicClass>();
                            s.AddHostedService<ApicService>();
                        })
                        .Build()
                        .RunAsync();
                    return;
                }

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


    class ApicService : BackgroundService
    {
        private readonly ApicClass _server;

        public ApicService(ApicClass server)
        {
            _server = server;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() => _server.Gateway(), stoppingToken);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            Log.Information("Service stopping - closing QuickBooks session");
            _server.Close_qb_session();
            await base.StopAsync(cancellationToken);
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
        public string Session { get; set; }
        public TomlTable Config { get; set; }

        private const int QbFileOpenMultiUser = 2;
        private const int GateTimeoutMs = 600000;
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        internal static TomlTable StaticConfig;

        private static string Read_token_from_config()
        {
            try
            {
                if (StaticConfig == null) { return string.Empty; }
                return StaticConfig["token"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Generate_token()
        {
            byte[] bytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static bool Is_authorized(HttpContext ctx)
        {
            string token = Environment.GetEnvironmentVariable("APIC_TOKEN");
            if (string.IsNullOrEmpty(token))
            {
                token = Read_token_from_config();
            }
            if (string.IsNullOrEmpty(token))
            {
                return true;
            }
            try
            {
                string header = ctx.Request.Headers["X-APIc-Token"];
                return !string.IsNullOrEmpty(header) && header == token;
            }
            catch (Exception ex)
            {
                Log.Information("Is_authorized: header read failed: " + ex.Message);
                return false;
            }
        }

        private static async Task Send_unauthorized(HttpContext ctx)
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers["Content-Type"] = "application/json";
            await ctx.Response.Send("{\"error\":\"unauthorized\"}");
        }

        private sealed class StaWorkItem
        {
            public Func<object> Work;
            public TaskCompletionSource<object> Tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Run()
            {
                try { Tcs.SetResult(Work()); } catch (Exception e) { Tcs.SetException(e); }
            }
        }

        private static readonly BlockingCollection<StaWorkItem> StaQueue = new BlockingCollection<StaWorkItem>();

        static ApicClass()
        {
            Thread sta = new Thread(() =>
            {
                foreach (var item in StaQueue.GetConsumingEnumerable())
                {
                    try { item.Run(); } catch { }
                }
            });
            sta.IsBackground = true;
            sta.Name = "QbSta";
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
        }

        private static T RunOnSta<T>(Func<T> work)
        {
            var item = new StaWorkItem
            {
                Work = () => work()
            };
            StaQueue.Add(item);
            return (T)item.Tcs.Task.GetAwaiter().GetResult();
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



        public dynamic Ensure_session()
        {
            if (Rp != null && Session != null)
            {
                Log.Information("Ensure_session: reusing ticket " + Session);
                return Rp;
            }
            Log.Information("Ensure_session: creating new connection and session");
            Rp = CreateRequestProcessor();
            Log.Information("Ensure_session: calling OpenConnection");
            Rp.OpenConnection("APIc", "APIc");
            Log.Information("Ensure_session: OpenConnection returned");
            Log.Information("Ensure_session: calling BeginSession (mode " + QbFileOpenMultiUser + " = multi-user)");
            Session = Rp.BeginSession(Config["quickbooks_file"].ToString(), QbFileOpenMultiUser);
            Log.Information("Ensure_session: BeginSession OK, ticket " + Session);
            return Rp;
        }

        public void Mark_session_dead()
        {
            try { if (Rp != null) Rp.CloseConnection(); } catch { }
            Rp = null;
            Session = null;
        }

        public void Close_qb_session()
        {
            try
            {
                if (Rp != null && Session != null)
                {
                    Log.Information("Close_qb_session: EndSession and CloseConnection");
                    Rp.EndSession(Session);
                    Rp.CloseConnection();
                }
            }
            catch (Exception ex) { Log.Information("Close_qb_session failed (best effort): " + ex.Message); }
            Rp = null;
            Session = null;
        }

        public ApicClass()
        {
            Config = Get_config();
            StaticConfig = Config;
            string logFile = Path.Combine(AppContext.BaseDirectory, "logs", "apic_server.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            Log.Logger = new LoggerConfiguration()
                              .MinimumLevel.Debug()
                            .WriteTo.Console(

                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                            .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
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
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                Close_qb_session();
            };
            if (Environment.UserInteractive)
            {
                Console.ReadLine();
            }



        }


 

        async Task Post(HttpContext ctx)
        {
            if (!Is_authorized(ctx))
            {
                await Send_unauthorized(ctx);
                return;
            }

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
                bool x = Validate_xml(i);
                if (x == false)
                {
                    Log.Information("Chunk #" + xmls.IndexOf(i) + " INVALID (" + (i == null ? 0 : i.Length) + " chars)");
                    Log.Information("Chunk content: " + (i != null && i.Length > 300 ? i.Substring(0, 300) + "..." : i));
                    ok = false;
                }
            }
            Log.Information("POST validation: " + (ok ? "all " + xmls.Count + " chunk(s) valid" : "validation FAILED"));

            if (!ok)
            {
                Log.Information("POST batch rejected - one or more chunks failed validation, QuickBooks NOT called");
            }


            if (ok)
            {
                if (!await Gate.WaitAsync(GateTimeoutMs))
                {
                    Log.Information("QuickBooks busy - gate timeout after 600s");
                    data["error"] = "QuickBooks is busy - timeout after 600s";
                }
                else
                {
                    try
                    {
                        Log.Information("Complete Batch XML is ok, sending " + xmls.Count + " request(s) to quickbooks");
                        List<string> rxml = Send_xmls(xmls);
                        Log.Information("POST: QuickBooks answered with " + rxml.Count + " response(s)");
                        data["responses"] = Parse_batch_respond(rxml);
                    }
                    finally
                    {
                        Gate.Release();
                    }
                }
            }

            ctx.Response.Headers["Content-Type"] = "application/json";

            string json = JsonConvert.SerializeObject(data);
            Log.Information("POST response: " + json.Length + " chars");

            await ctx.Response.Send(json);
        }

        async Task Test_route(HttpContext ctx)
        {
            if (!Is_authorized(ctx))
            {
                await Send_unauthorized(ctx);
                return;
            }

            IDictionary<string, string> data = new Dictionary<string, string>();

            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["Content-Type"] = "application/json";
            Log.Information("Visitor: " + ctx.Request.Useragent);
            Log.Information("Request: /test/");
            if (!await Gate.WaitAsync(GateTimeoutMs))
            {
                Log.Information("QuickBooks busy - gate timeout after 600s");
                data["testing quickbooks file access"] = "False";
                data["message"] = "QuickBooks is busy - timeout after 600s";
                data["quickbooks_file"] = Config["quickbooks_file"];
            }
            else
            {
                try
                {
                    bool test = Test_qb_access();
                    Log.Information("Test result: " + test);
                    data["testing quickbooks file access"] = test.ToString();
                    data["quickbooks_file"] = Config["quickbooks_file"];
                }
                finally
                {
                    Gate.Release();
                }
            }
            string json = JsonConvert.SerializeObject(data);

            await ctx.Response.Send(json);
        }

        async Task Default_route(HttpContext ctx)
        {
            try
            {
            if (!Is_authorized(ctx))
            {
                await Send_unauthorized(ctx);
                return;
            }

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

                    if (!await Gate.WaitAsync(GateTimeoutMs))
                    {
                        Log.Information("QuickBooks busy - gate timeout after 600s");
                        data = Set_json_msg("QuickBooks is busy - timeout after 600s");
                    }
                    else
                    {
                        try
                        {
                            string rxml = Send_xml(xml);
                            Log.Information("Quickbooks responded, sending response to parase it\t" + Request_counter);
                            data = ParseRespond(rxml);
                        }
                        finally
                        {
                            Gate.Release();
                        }
                    }
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
            string response = null;

            List<string> responses = new List<string>();

            Log.Information("Send_xmls_impl: entering, " + xmls.Count + " request(s), company file: " + qbfile);
            try
            {
                Ensure_session();

                int c = xmls.Count;
                int done = 0;
                foreach (var i in xmls)
                {
                    if (done == 0 || (done + 1) % 50 == 0 || c == 1)
                    {
                        Log.Information("process " + (done + 1) + " of " + xmls.Count + " (payload " + (i == null ? 0 : i.Length) + " chars)");
                    }

                    response = Rp.ProcessRequest(Session, i);
                    responses.Add(response);
                    c--;
                    done++;
                }
            }
            catch (Exception ex)
            {
                string error = "COM Error Description = " + ex.Message + "COM error";
                Console.WriteLine(error);
                Log.Information("Send_xmls_impl failed: " + ex.ToString());
                Mark_session_dead();
                responses.Add(error);
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
            string response = null;

            Log.Information("Send_xml_impl: entering, payload length: " + (xml_str == null ? 0 : xml_str.Length) + ", company file: " + qbfile);
            try
            {
                Ensure_session();
                Log.Information("Send_xml_impl: calling ProcessRequest");
                response = Rp.ProcessRequest(Session, xml_str);
                Log.Information("Send_xml_impl: ProcessRequest returned (" + (response == null ? 0 : response.Length) + " chars)");
            }
            catch (Exception ex)
            {
                Log.Information("Send_xml_impl failed: " + ex.ToString());
                Mark_session_dead();
                return "COM Error Description = " + ex.Message + "COM error";
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
                try
                {
                    Ensure_session();
                    Log.Information("Access granted (ticket reused/created: " + Session + ")");
                    r = true;
                }
                catch (Exception ex)
                {
                    Log.Information("Error");
                    Log.Information(ex.ToString());
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

            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "apic_config.toml");            string[] argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length - 1; i++)
            {
                if (argv[i] == "--config") { path = argv[i + 1]; break; }
            }
            string envPath = Environment.GetEnvironmentVariable("APIC_CONFIG");
            if (!string.IsNullOrEmpty(envPath)) { path = envPath; }
            Config_file = path;
            Console.WriteLine("...config path: " + path);
            if (!File.Exists(path))
            {
                string legacy = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "apic_config.toml");
                if (File.Exists(legacy))
                {
                    Console.WriteLine("...migrating config from " + legacy);
                    File.Copy(legacy, path);
                }
                else
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
                    ["token"] = Generate_token(),


                };
                using (StreamWriter writer = File.CreateText(path))
                {
                    toml.WriteTo(writer);
                    writer.Flush();
                }
                Console.WriteLine("...generated a random token (also in the config): " + toml["token"]);
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
                    string tok = null;
                    try { tok = config["token"]?.ToString(); } catch { }
                    if (string.IsNullOrEmpty(tok))
                    {
                        tok = Generate_token();
                        try
                        {
                            List<string> lines = new List<string>(File.ReadAllLines(path));
                            bool found = false;
                            for (int i = 0; i < lines.Count; i++)
                            {
                                if (lines[i].TrimStart().StartsWith("token"))
                                {
                                    lines[i] = "token = \"" + tok + "\"";
                                    found = true;
                                    break;
                                }
                            }
                            if (!found) { lines.Add("token = \"" + tok + "\""); }
                            File.WriteAllLines(path, lines);
                            Console.WriteLine("...generated a random token (saved to config): " + tok);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("...could not save generated token to config: " + ex.Message);
                        }
                        config["token"] = tok;
                    }
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


