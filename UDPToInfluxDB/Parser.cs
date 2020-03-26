//#define VERBOSE


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UDPToInfluxDB;

namespace TeslaSCAN {

  [Serializable]
  public class Parser {
    private Program program;
    public SortedList<int, Packet> packets;
    long time; // if I was faster I'd use 'short time'.... :)
    int numUpdates;
    int numCells;
    public char[] tagFilter;
    public bool fastLogEnabled;
    private StreamWriter fastLogStream;
    char separator = ',';
    Stopwatch logTimer;


    public const double miles_to_km = 1.609344;
    public const double kw_to_hp = 1.34102209;
    public const double nm_to_ftlb = 0.737562149;
    double nominalFullPackEnergy;
    double amp;
    double volt;
    double power;
    double mechPower;
    double fMechPower;
    double speed;
    double drivePowerMax;
    double torque;
    double chargeTotal;
    double dischargeTotal;
    double odometer;
    double tripDistance;
    double charge;
    double discharge;

    private double frTorque;
    private double dcChargeTotal;
    private double acChargeTotal;
    private double regenTotal;
    private double energy;
    private double regen;
    private double acCharge;
    private double dcCharge;
    private double nominalRemaining;
    private double buffer;
    private double soc;
    private double fl;
    private double fr;
    private double rl;
    private double rr;
    private int frpm;
    private int rrpm;
    private bool feet;
    private bool seat;
    private bool win;
    private long resetGaugeTime;
    private int dcIn;
    private double dcOut;
    private double fDissipation;
    private double combinedMechPower;
    private double rDissipation;
    private double rInput;
    private double fInput;
    private double hvacPower;
    private bool dissipationUpdated;
    private long dissipationTimeStamp;


    static UInt64 ByteSwap64(UInt64 n) {
      UInt64 n_swapped = 0;
      for (int byte_index = 7; byte_index >= 0; --byte_index) {
        n_swapped <<= 8;
        n_swapped |= n % (1 << 8);
        n >>= 8;
      }
      return n_swapped;
    }

    public static double? ExtractSignalFromBytes(byte[] bytes, int StartBit, int BitSize, bool signed, double ScaleFactor, double Offset, bool bigEndian = false) {
      UInt64 signalMask = 0;

      if (StartBit + BitSize > bytes.Length * 8) // check data length
        return null;

      for (int bit_index = (int)(StartBit + BitSize - 1); bit_index >= 0; --bit_index) {
        signalMask <<= 1;
        if (bit_index >= StartBit) {
          signalMask |= 1;
        }
      }

      UInt64 signalValueRaw = 0;
      for (int byte_index = bytes.Length - 1; byte_index >= 0; --byte_index) {
        signalValueRaw <<= 8;
        signalValueRaw += bytes[byte_index];
      }

      signalValueRaw &= signalMask;

      if (bigEndian) {
        signalMask = ByteSwap64(signalMask);
        signalValueRaw = ByteSwap64(signalValueRaw);
      }

      while ((signalMask & 0x1) == 0) {
        signalValueRaw >>= 1;
        signalMask >>= 1;
      }

      double signalValue = signalValueRaw;

      if (signed) {
        UInt64 signalMaskHighBit = (signalMask + 1) >> 1;
        if ((signalValueRaw & signalMaskHighBit) != 0) {
          signalValue = -(Int64)((signalValueRaw ^ signalMask) + 1);
        }
      }

      signalValue *= ScaleFactor;
      signalValue += Offset;

      return signalValue;
    }

    public static double? ExtractSignalFromBytes(byte[] bytes, int StartBit, int BitSize, bool signed, double ScaleFactor, double Offset) {
      UInt64 signalMask = 0;

      if (StartBit + BitSize > bytes.Length * 8) // check data length
        return null;

      for (int bit_index = (int)(StartBit + BitSize - 1); bit_index >= 0; --bit_index) {
        signalMask <<= 1;
        if (bit_index >= StartBit) {
          signalMask |= 1;
        }
      }

      UInt64 signalValueRaw = 0;
      for (int byte_index = bytes.Length - 1; byte_index >= 0; --byte_index) {
        signalValueRaw <<= 8;
        signalValueRaw += bytes[byte_index];
      }

      signalValueRaw &= signalMask;

      /*if (signal.ByteOrder == Message.Signal.ByteOrderEnum.BigEndian) {
        signalMask = ByteSwap64(signalMask);
        signalValueRaw = ByteSwap64(signalValueRaw);
      }*/

      while ((signalMask & 0x1) == 0) {
        signalValueRaw >>= 1;
        signalMask >>= 1;
      }

      double signalValue = signalValueRaw;

      if (signed) {
        UInt64 signalMaskHighBit = (signalMask + 1) >> 1;
        if ((signalValueRaw & signalMaskHighBit) != 0) {
          signalValue = -(Int64)((signalValueRaw ^ signalMask) + 1);
        }
      }

      signalValue *= ScaleFactor;
      signalValue += Offset;

      return signalValue;
    }


    public Parser(Program prog) {

      program = prog;
      //items = new ConcurrentDictionary<string, ListElement>();
      packets = new SortedList<int, Packet>();
      //time = SystemClock.ElapsedRealtime() + 1000;

      /* tags:
          p: performance
          t: trip
          b: battery
          c: temperature
          f: front drive unit
          s: startup (app will wait until these packets are found before starting 'normal' mode)
          i: ignore (in trip tabs, with slow/ELM adapters)
          z: BMS
          x: Cells
          e: efficiency
      */

    }


    private void ParsePacket(string raw, int id, byte[] bytes) {
      if (packets.ContainsKey(id)) {
        packets[id].Update(bytes);
      }
    }

    public void UpdateItem(string name, string unit, string tag, int index, double value, int id) {

      if (!Double.IsInfinity(value) && !Double.IsNaN(value)) { // influxDB does not support Infinity. Let's not waste time by getting that error back
        program.SendToDBAsync(name, value, program.file, program.timestamp);
      }
    }



    public List<Value> GetValues(string tag) {
      var charArray = tag.ToCharArray(); // I'll cache it to be nice to the CPU cycles
      tagFilter = charArray;
      List<Value> values = new List<Value>();
      foreach (var packet in packets)
        foreach (var value in packet.Value.values)
          if (value.tag.IndexOfAny(charArray) >= 0 || tag=="")
            values.Add(value);

      return values
        .OrderBy(x => x.index)
        //.OrderBy(x => x.unit.Trim())
        .ToList();
    }


    public string[] GetCANFilter(List<Value> items) {
      var f=items.FirstOrDefault();
      int filter=0;
      if (f != null)
        filter = f.packetId.First();
      int mask = 0;

      List<int> ids = new List<int>();
      foreach (var item in items)
        foreach (var id in item.packetId)
          if (!ids.Exists(x => x == id))
            ids.Add(id);

      foreach (var id in ids) {
        for (int bit = 0; bit < 11; bit++)
          if (((id >> bit) & 1) != ((filter >> bit) & 1)) {
            mask |= 1 << bit;
            //filter &= ~(1 << bit);
          }
      }
      mask = ~mask & 0x7FF;
      Console.WriteLine(Convert.ToString(mask, 2).PadLeft(11, '0'));
      Console.WriteLine("{0,4} filter: {1,3:X} mask: {2,3:X}", 1, filter, mask, 1, 1);
      List<string> result = new List<string>();
      result.Add(Convert.ToString(mask, 16));
      result.Add(Convert.ToString(filter, 16));
      foreach (int id in ids)
        result.Add(Convert.ToString(id, 16));
      return result.ToArray();
    }

    public string[] GetCANFilter(string tag) {
      int filter = 0;
      int mask = 0;
      List<int> ids = new List<int>();
      foreach (var packet in packets.Values)
        foreach (var value in packet
          .values
          .Where(x => x.tag.IndexOfAny(tag.ToCharArray()) >= 0 || tag==""))
          if (!ids.Exists(x=>x == packet.id))
          ids.Add(packet.id);

      if (tag.Contains('z'))
        ids.Add(0x6F2);     

      foreach (var id in ids) {
        if (filter == 0)
          filter = id;
        for (int bit = 0; bit < 11; bit++)
          if (((id >> bit) & 1) != ((filter >> bit) & 1)) {
            mask |= 1 << bit;
            //filter &= ~(1 << bit);
          }
      }
      mask = ~mask & 0x7FF;
      Console.WriteLine(Convert.ToString(mask, 2).PadLeft(11, '0'));
      Console.WriteLine("{0,4} filter: {1,3:X} mask: {2,3:X}", 1, filter, mask, 1, 1);
      List<string> result = new List<string>();
      result.Add(Convert.ToString(mask, 16));
      result.Add(Convert.ToString(filter, 16));
      foreach (int id in ids)
        result.Add(Convert.ToString(id, 16));
      return result.ToArray();
    }

    // returns true IF startup=true AND all packets tagged with 's' have been received.

    public List<int> GetCANids(string tag) {
      List<int> ids = new List<int>();
      foreach (var packet in packets.Values)
        foreach (var value in packet
          .values
          .Where(x => x.tag.IndexOfAny(tag.ToCharArray()) >= 0 || tag == ""))
          if (!ids.Exists(x => x == packet.id))
            ids.Add(packet.id);
      return ids;
    }


    public bool Parse(string input, int idToFind) {
      if (!input.Contains('\n'))
        return false;
      if (input.StartsWith(">"))
        input = input.Substring(1);
      List<string> lines = input?.Split('\n').ToList();
      lines.Remove(lines.Last());

      bool found = false;

      foreach (var line in lines)
        try {
          /*if (par)
          if ((!(line.Length == 9 && (line.StartsWith("26A")))) &&
              (!(line.Length == 11 && (line.StartsWith("5D8") || line.StartsWith("562") || line.StartsWith("232") || line.StartsWith("26A")))) &&
              (!(line.Length == 15 && (line.StartsWith("116") || line.StartsWith("222")))) &&
              (!(line.Length == 17 && (line.StartsWith("210") || line.StartsWith("115")))) &&
               line.Length != 19) { // testing an aggressive garbage collector! // 11)
#if VERBOSE
            Console.WriteLine("GC " + line);
#endif
            continue;
          }*/
#if VERBOSE
          Console.WriteLine(line);
#endif
          int id = 0;
          if (!int.TryParse(line.Substring(0,3), System.Globalization.NumberStyles.HexNumber, null, out id))
            continue;
          string[] raw = new string[(line.Length - 3) / 2];
          int r = 0;
          int i;
          for (i = 3; i < line.Length-1; i += 2)
            raw[r++] = line.Substring(i,2);
          List<byte> bytes = new List<byte>();
          i = 0;
          byte b = 0;
          for (i = 0; i < raw.Length; i++)
            if (raw[i].Length != 2 || !byte.TryParse(raw[i], System.Globalization.NumberStyles.HexNumber, null, out b))
              break;
            else bytes.Add(b);
#if disablebluetooth
          if (fastLogEnabled)
            fastLogStream.WriteLine(line);
#endif
          if (bytes.Count == raw.Length) { // try to validate the parsing 
            ParsePacket(line, id, bytes.ToArray());
            //MainActivity.bluetoothHandler.ResetTimeout();
            if (idToFind>0)
              if (idToFind == id)
                found=true;
          }
        } catch (Exception e) { /*Console.WriteLine(e.ToString());*/ };

      /*if (startup) {
        bool foundAll = true;
        foreach (var p in packets)
          foreach (var v in p.Value.values)
            if (v.tag.Contains('s') &&
            !items.ContainsKey(v.name)) {
              foundAll = false;
              break;
            }
        return foundAll;
      }*/
      if (found) return true;
      return false;
    }


  }
}

