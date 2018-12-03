using System;
using BarnardTech.WebSocketComms;

namespace ExampleHttpServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Example Webserver with WebSockets communication.");

            using (HttpCommsServer webServer = new HttpCommsServer("example", 8080, true, true))
            {
                webServer.AddRoute("comms", new commandLink());
                webServer.AddEmbeddedContent(typeof(wwwroot.Content));

                Console.WriteLine("Webserver running on http://localhost:8080/");
                Console.WriteLine("Press enter to exit.");
                Console.ReadLine();
            }
        }
    }
    class commandLink
    {

    }
}
