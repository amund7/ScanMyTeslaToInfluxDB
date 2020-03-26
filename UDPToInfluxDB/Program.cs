using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CANBUS;
using TeslaSCAN;

namespace UDPToInfluxDB {
  public class Program {

    private static readonly HttpClient client = new HttpClient();
    public long timestamp;
    public string file;
    public int queueLength;
    private StringBuilder content = new StringBuilder ();
    private int numWritten;
    private string VIN;
    private string SW;
    private string BatterySerial;
    private static Parser parser;

    public static void Main(string[] args) {

      System.Threading.Thread.CurrentThread.CurrentCulture =
        System.Globalization.CultureInfo.InvariantCulture;


      var p = new Program();

      foreach (var arg in args) {
        if (arg.ToLower() == "-models")
          parser = new CANBUS.ModelSPackets(p);
        else
        if (arg.ToLower() == "-model3")
          parser = new CANBUS.Model3Packets(p);
        else {
          /*Console.WriteLine(Path.GetFullPath(arg));
          Console.WriteLine(Path.GetFileName(arg));*/

          foreach (var f in Directory.GetFiles(arg,"*.txt").Reverse()) {
            Console.WriteLine(f);
            if (Path.GetExtension(f).ToLower()==".txt")
              p.ReadRAW(f).Wait();
            if (Path.GetExtension(f).ToLower() == ".csv")
              p.ReadCSV(f).Wait();
          }
        }
      }

      //DirectoryInfo dir = new DirectoryInfo(@"G:\Tesla logs");
      //DirectoryInfo dir = new DirectoryInfo(@"F:\Prog\Google Drive\Tesla\Logger\ScanMyModel3\Annas tlf");


    }

    async Task ReadCSV(string filename) {
      try {
        Console.Write(filename);
        var f = File.OpenRead(filename);
        var stream = new StreamReader(f);
        string header = stream.ReadLine();
        while (header.TrimStart().StartsWith("//")) {
          //title += header;
          header = stream.ReadLine(); // VESC MOnitor first line is a summary/comment
        }
        var columns = header.Split(',');
        int columnCount = columns.Count();
        numWritten = 0;

        string file = Path.GetFileName(f.Name);
        string date = file.Substring(file.IndexOf('2'));
        date = date.Substring(0, date.IndexOf('.'));
        DateTime startTime = DateTime.ParseExact(date, "yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        long timestamp = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();

        file = file.Replace(" ", "_");

        while (!stream.EndOfStream) {
          var line = stream.ReadLine();
          int i = 0;
          int t = 0;

          foreach (var c in line.Split(',')) {
            if (i == 0) {
              int.TryParse(c, out t);
              i++;
              //Console.WriteLine(timestamp + t);
              continue;
            }
            double d = 0;

            if (double.TryParse(c, out d)) {
              if (!Double.IsInfinity(d) && !Double.IsNaN(d)) { // influxDB does not support Infinity. Let's not waste time by getting that error back
                SendToDBAsync(columns[i], d, file, timestamp + t).Wait();
              }
            }
            i++;
          }
        }
        await WriteBufferToDB(content.ToString());
        content.Clear();
        queueLength = 0;
        Console.WriteLine();
      }
      catch (Exception e) { Console.WriteLine(e.Message); };
    }


      async Task ReadRAW(string filename) {
      try {
        Console.WriteLine(filename);
        numWritten = 0;

        //ScanForVIN(filename);
        Console.Write(" VIN:{0} FW:{1} Battery serial:{2} ", VIN, SW, BatterySerial);

        var f = File.OpenRead(filename);
        var stream = new StreamReader(f);

        file = Path.GetFileName(f.Name);
        string date = file.Substring(file.IndexOf('2'));
        date = date.Substring(0, date.IndexOf('.'));
        DateTime startTime = DateTime.ParseExact(date, "yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        timestamp = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();

        file = file.Replace(" ", "_");
        if (parser is Model3Packets)
          parser = new Model3Packets(this);
        else
          parser = new ModelSPackets(this);

        while (!stream.EndOfStream) {
          var line = stream.ReadLine();
          timestamp++;
          parser.Parse(line + '\n', 0);
        }
        await WriteBufferToDB(content.ToString());
        content.Clear();
        queueLength = 0;
        Console.WriteLine();
      }
      catch (Exception e) { Console.WriteLine(e.Message); };
    }

    private void ScanForVIN(string filename) {
      var f = File.OpenRead(filename);
      var stream = new StreamReader(f);
      VIN = "";
      SW = "";
      BatterySerial = "";
      SortedDictionary<int, char> batterySerial = new SortedDictionary<int, char>();

      while (!stream.EndOfStream) {
        string line = stream.ReadLine();
        string s;
        if (line.Length > 21)
          s = line.Substring(0, 21);
        else s = line;
        for (int i = 3; i < s.Length; i += 3)
          s = s.Insert(i, " ");


        if (s.StartsWith("508")) {
          try {
            var vin = new StringBuilder(VIN.PadLeft(17));
            int temp, idx;
            int.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out idx);
            for (int i = 7; i < s.Length; i += 3)
              if (int.TryParse(s.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, null, out temp))
                if (temp != 0)
                  vin[idx * 7 + (i / 3) - 2] = (char)temp;

            VIN = vin.ToString();
          }
          catch (Exception e) { }
        }

        if (s.StartsWith("542") || s.StartsWith("552")) {
          try {
            StringBuilder vin = new StringBuilder(" ".PadLeft(16));
            int temp, idx;
            idx = s.StartsWith("542") ? 0 : 8;
            for (int i = 4; i < s.Length; i += 3)
              if (int.TryParse(s.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, null, out temp))
                if (temp != 0)
                  batterySerial[idx * 8 + (i / 3) - 1] = (char)temp;

            BatterySerial = new string(batterySerial.Values.ToArray());
          }
          catch (Exception e) { }
        }

        if (s.StartsWith("558")) {
          try {
            StringBuilder vin = new StringBuilder(" ".PadLeft(8));
            int temp;
            for (int i = 4; i < s.Length; i += 3)
              if (int.TryParse(s.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, null, out temp))
                if (temp != 0)
                  vin[(i / 3) - 1] = (char)temp;

            SW = vin.ToString();
          }
          catch (Exception e) { }
        }
      }
    }

    public async Task SendToDBAsync(string name, double d, string file, long timestamp) {
      queueLength++;
      //numWritten++;
      content.Append(
        "measurement,name=" + name.Replace(" ", "\\ ") +
        ",file=" + file +
        /*(VIN=="" ? "" : ",vin=" + VIN) +
        (SW=="" ? "": ",fw=" + SW )+
        (BatterySerial==""? "" : ",batteryser=" + BatterySerial )+*/
        " value=" + d +
        " " + (timestamp).ToString() +
        "\n"
      );

      if (queueLength >= 100000) {
        WriteBufferToDB(content.ToString());
        content.Clear();
        queueLength = 0;
      }
    }

    public async Task WriteBufferToDB(string buffer) {
      var response = await client.PostAsync("http://localhost:8086/write?db=SMT&precision=ms", new StringContent(buffer));
      //var response = await client.PostAsync("http://localhost:8086/write?db=Model3&precision=ms", new StringContent(buffer));
      var responseString = await response.Content.ReadAsStringAsync();
      Console.Write(responseString);
      numWritten += 100;
      Console.Write("\r{0}k ", numWritten);
    }


  }
}
