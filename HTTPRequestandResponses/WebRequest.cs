using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
namespace ConsoleApplication16
{
    class Program
    {
        static void Main()
        {
            MainAsync().Wait();
        }
        static async Task MainAsync()
        {
            //This may or may not be required depending upon the type of protocol the application is running. With .Net Framework 4.6 or above and application TLS 1.2 above this is not required.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            try
            {
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Default.GetBytes(apitoken + ":X")));
                HttpResponseMessage ChildsResponse = client.GetAsync(URL).Result;
                ChildsResponse.EnsureSuccessStatusCode();
                dynamic ChildsResponseData = await ChildsResponse.Content.ReadAsStringAsync();
                Console.Out.WriteLine(ChildsResponseData);
                Console.ReadKey();
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadKey();
            }
        }
    }
}
