using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using HtmlAgilityPack;
using System.Threading.Tasks;

namespace ParcelMotelChecker
{
    public class ParcelMotelObject
    {
        public DateTime CheckedIn { get; set; }

        public string Image { get; set; }

        public string Location { get; set; }

        public string NotificationEmail { get; set; }

        public string NotificationMobile { get; set; }

        public string PIN { get; set; }

        public string Status { get; set; }

        public DateTime StatusTime { get; set; }

        public string TrackingNumber { get; set; }
    }

    internal class Program
    {
        public static CookieContainer Login(string userName, string password)
        {
            CookieContainer tmp = new CookieContainer();
            string LoginURL = "https://www.parcelmotel.com/MyParcelMotel/Account/RemoteLogin";
            string ToPost = String.Format("UserName={0}&Password={1}&RememberMe=false", userName, password);
            using (HttpWebResponse Response = Request(LoginURL, ToPost, null, "http://www.parcelmotel.com"))
            {
                tmp.Add(Response.Cookies);
                Response.Close();
            }
            return tmp;
        }

        private static void Main(string[] args)
        {
            string userName = ConfigurationManager.AppSettings["Username"];
            string password = ConfigurationManager.AppSettings["Password"];
            CookieContainer cookies;
            try
            {
                cookies = Login(userName, password);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error getting login details...{0}... ending...",ex.Message);
                return;
            }
            List<ParcelMotelObject> packages = new List<ParcelMotelObject>();
            for (int i = 1; i <= 25; i++)
            {
                try
                {
                    var stream = Request("http://www.parcelmotel.com/MyParcelMotel/Member/PackageHistoryPage?Page=" + i, null, cookies, "https://www.parcelmotel.com/MyParcelMotel/").GetResponseStream();

                    string webtext = StreamToString(stream);
                    var result = ParseHTML(webtext);
                    packages.AddRange(result);
                    if (result.Count == 0)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error getting text from Parcel Motel...: {0}", ex.Message);
                }

            }
            Console.WriteLine("Found a total of {0} packages", packages.Count);

            string pushoverAPIKey = ConfigurationManager.AppSettings["POAPIKey"];
            string pushoverDevice = ConfigurationManager.AppSettings["PODevice"];
            //send notification...
            PushoverClient.Pushover po = new PushoverClient.Pushover(pushoverAPIKey);

            foreach (ParcelMotelObject obj in packages)
            {
                string packageFile = string.Format("{0}.txt", obj.TrackingNumber);
                if (!File.Exists(packageFile))
                {
                    if (obj.Status != "Delivered")
                    {
                        string message = string.Format("Parcel {0} is new. Status: {1} Date: {2}", obj.TrackingNumber, obj.Status, obj.StatusTime);
                        var resp = po.Push("New Parcel Motel Package", message, pushoverDevice);
                        Console.WriteLine(resp.Status);
                    }
                    File.WriteAllText(packageFile, string.Format("{0};{1}", obj.Status, obj.StatusTime));
                }
                else
                {
                    string data = File.ReadAllText(packageFile);
                    string[] oldData = data.Split(';');
                    string oldStatus = oldData[0];
                    DateTime oldDate = DateTime.Parse(oldData[1]);

                    if (obj.Status != oldStatus)
                    {
                        string message = string.Format("Parcel {0} status changed from {1} to {2} at {3}", obj.TrackingNumber, oldStatus, obj.Status, obj.StatusTime);
                        var resp = po.Push("New Parcel Motel Package", message, pushoverDevice);
                        Console.WriteLine(resp.Status);
                        File.WriteAllText(packageFile, string.Format("{0};{1}", obj.Status, obj.StatusTime)); // fixing bug #1 on github
                    }
                }
            }

            var locations = packages.GroupBy(x => x.Location);
            var statuses = packages.GroupBy(x => x.Status);
            foreach (var location in locations)
            {
                Console.WriteLine("location: {0} number: {1}", location.Key, location.Count());
            }
            foreach (var status in statuses)
            {
                Console.WriteLine("Status: {0} number: {1}", status.Key, status.Count());
            }

          
        }

        private static List<ParcelMotelObject> ParseHTML(string webtext)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(webtext);

            List<ParcelMotelObject> packages = new List<ParcelMotelObject>();
            ParcelMotelObject parcel = new ParcelMotelObject();
            int count = 0;

            var nodes = doc.DocumentNode.SelectNodes("//tr[@class='memberReportRowAlternate']//td | //tr[@class='memberReportRow']//td");
            if (nodes == null)
            {
                Console.WriteLine("No parcels found...");
                return new List<ParcelMotelObject>();
            }

            foreach (HtmlNode tdNode in nodes)
            {
                switch (count % 9)
                {
                    case 0:
                        parcel.Image = tdNode.InnerText.Trim();
                        break;

                    case 1:
                        parcel.CheckedIn = DateTime.Parse(tdNode.InnerText.Trim());
                        break;

                    case 2:
                        parcel.Location = tdNode.InnerText.Trim();
                        break;

                    case 3:
                        parcel.TrackingNumber = tdNode.InnerText.Trim();
                        break;

                    case 4:
                        parcel.NotificationMobile = tdNode.InnerText.Trim();
                        break;

                    case 5:
                        parcel.NotificationEmail = tdNode.InnerText.Trim();
                        break;

                    case 6:
                        parcel.PIN = tdNode.InnerText.Trim();
                        break;

                    case 7:
                        parcel.Status = tdNode.InnerText.Trim();
                        break;

                    case 8:
                        parcel.StatusTime = DateTime.Parse(tdNode.InnerText.Trim());
                        packages.Add(parcel);
                        parcel = new ParcelMotelObject();
                        break;
                }
                count++;
            }
            Console.WriteLine("Found {0} packages", packages.Count);
            return packages;
        }

        private static HttpWebResponse Request(string URL, string ToPost, CookieContainer Cookies, string Referrer)
        {
            System.Net.ServicePointManager.Expect100Continue = false;
            HttpWebRequest Request = (HttpWebRequest)WebRequest.Create(URL);
            Request.CookieContainer = new CookieContainer();
            Request.Referer = Referrer;
            Request.UserAgent = "Mozilla/5.0 (Windows; U; Windows NT 5.1; en-US; rv:1.8.0.6) Gecko/20060728 Firefox/1.5.0.6";

            if (Cookies != null)
                Request.CookieContainer.Add(Cookies.GetCookies(Request.RequestUri));
            if (ToPost != null)
            {
                Request.Method = "POST";
                Request.ContentType = "application/x-www-form-urlencoded";
                byte[] data = System.Text.Encoding.UTF8.GetBytes(ToPost);
                Request.ContentLength = data.Length;
                System.IO.Stream writeStream = Request.GetRequestStream();
                writeStream.Write(data, 0, data.Length);
                writeStream.Close();
            }
            else
            {
                Request.Method = "GET";
            }
            HttpWebResponse Response =  (HttpWebResponse)(Request.GetResponse());
            Response.Cookies = Request.CookieContainer.GetCookies(Request.RequestUri);
            return Response;
        }

        private static string StreamToString(System.IO.Stream readStream)
        {
            string result = null;
            string tempstring = null;
            int count = 0;
            byte[] buffer = new byte[8192];
            do
            {
                count = readStream.Read(buffer, 0, buffer.Length);
                if (count != 0)
                {
                    tempstring = System.Text.Encoding.ASCII.GetString(buffer, 0, count);
                    result = result + tempstring;
                }
            }
            while (count > 0);
            readStream.Close();
            return result;
        }
    }
}