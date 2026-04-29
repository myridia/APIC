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
	    public TomlTable Config {get;set;}


      public ApicClass()
	    {
    Log.Logger = new LoggerConfiguration()
	                  .MinimumLevel.Debug()  
                    .WriteTo.Console(
            // Optional: Define the output template for the console
            // This is often simpler than file output
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
	                  .CreateLogger();


      }
	  public void Gateway()
	        {

 	            string logo = @"
	             _____    _____        
	     /\     |  __ \  |_   _|       
	    /  \    | |__) |   | |     ___ 
	   / /\ \   |  ___/    | |    / __|
	  / ____ \  | |       _| |_  | (__ 
	 /_/    \_\ |_|      |_____|  \___|";
   
               Log.Information(logo);

          }
 
}


}


