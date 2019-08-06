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
    SortedList<int, Packet> packets;
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

      Packet p;

      /*packets.Add(0x256, p = new Packet(0x256, this));
      p.AddValue("Metric", "bool", "s", (bytes) => {
        metric = Convert.ToBoolean(bytes[3] & 0x80);
        if (metric) {
          foreach (var packet in packets)
            foreach (var v in packet.Value.values)
              if (v.tag.Contains("i"))
                packet.Value.values.Remove(v);
        } else {
          foreach (var packet in packets)
            foreach (var v in packet.Value.values)
              if (v.tag.Contains("m"))
                packet.Value.values.Remove(v);
        } 
        return metric ? 1 : 0;
      });*/

      packets.Add(0x102, p = new Packet(0x102, this));
      p.AddValue("Battery voltage", " V", "bpr", (bytes) => volt =
          (bytes[0] + (bytes[1] << 8)) / 100.0);
      p.AddValue("Battery current", " A", "b", (bytes) => amp =
          1000 - ((Int16)((((bytes[3] & 0x7F) << 8) + bytes[2]) << 1)) / 20.0);
      p.AddValue("Battery power", " kW", "bpe", (bytes) => power = amp * volt / 1000.0);
      //p.AddValue("cell average", "Vc", "bp", (bytes) => numCells > 70 ? volt / numCells : bytes[100]);
      //p.AddValue("negative terminal", "C", (bytes) => ((bytes[6] + ((bytes[7] & 0x07) << 8))) * 0.1 - 10);

      packets.Add(0x31A, p = new Packet(0x31A, this));
      p.AddValue("Battery inlet", "C", "c",
        //(bytes) => ((bytes[0] + ((bytes[1] & 0x03) << 8)) / 10.0) - 30);
        (bytes) => (bytes[0] + ((bytes[1] & 0x03) << 8)) / 8.0 - 40);
      p.AddValue("PT inlet", "C", "c",
        //(bytes) => ((bytes[4] + ((bytes[5] & 0x03) << 8)) / 10.0) - 30);
        (bytes) => (bytes[4] + ((bytes[5] & 0x03) << 8)) / 8.0 - 40);

      packets.Add(0x26A, p = new Packet(0x26A, this));
      p.AddValue("Coolant heater exit", "C", "c",
        (bytes) => (bytes[0] + ((bytes[1] & 0x03) << 8) - 320) / 8.0);

      packets.Add(0x210, p = new Packet(0x210, this));
      p.AddValue("DC-DC current", "A12", "b", (bytes) => bytes[4]);
      p.AddValue("DC-DC voltage", "V12", "b", (bytes) => bytes[5] / 10.0);
      p.AddValue("DC-DC coolant inlet", "  C", "b", (bytes) => ((bytes[2] - (2 * (bytes[2] & 0x80))) * 0.5) + 40);
      p.AddValue("DC-DC input power", "W", "b", (bytes) => dcIn = (bytes[3] * 16));
      p.AddValue("12v systems", "W", "e", (bytes) => dcIn = (bytes[3] * 16));
      p.AddValue("DC-DC output power", "W", "b", (bytes) => dcOut = (bytes[4] * bytes[5] / 10.0));
      p.AddValue("DC-DC efficiency", "%", "e", (bytes) => dcOut / dcIn * 100.0);
      p.AddValue("400V systems", " kW", "e", (bytes) => power - dcIn/1000.0);
      p.AddValue("Heating/cooling", "kW", "eh", (bytes) => {
        if (dissipationUpdated/* || 
          SystemClock.ElapsedRealtime() > dissipationTimeStamp + 2000*/) {
          hvacPower = hvacPower * 0.99 + (power - (rInput + fInput) - (dcIn / 1000.0)) * 0.01;
          //hvacPower = (power - (rInput + fInput) - (dcIn / 1000.0));
          dissipationUpdated = false;
          return hvacPower;
        } else return (double?)null;
      } , new int[] { 0x102, 0x266, 0x2E5 });

      packets.Add(0x1D4, p = new Packet(0x1D4, this));
      p.AddValue("Fr torque measured", "Nm", "pf", (bytes) => frTorque =
         (bytes[5] + ((bytes[6] & 0x1F) << 8) - (512 * (bytes[6] & 0x10))) * 0.25);
      p.AddValue("Rr/Fr torque bias", "%", "pf", 
        (bytes) => Math.Abs(frTorque) + Math.Abs(torque) == 0 ? 50 : Math.Abs(torque) / (Math.Abs(frTorque) + Math.Abs(torque)) * 100 );

      packets.Add(0x154, p = new Packet(0x154, this));
      p.AddValue("Rr torque measured", "Nm", "p", (bytes) => torque =
         (bytes[5] + ((bytes[6] & 0x1F) << 8) - (512 * (bytes[6] & 0x10))) * 0.25);
      //p.AddValue("Pedal position A", "%", "",  (bytes) => bytes[2] * 0.4);
      p.AddValue("Watt pedal", "%", "", (bytes) => bytes[3] * 0.4);
      /*p.AddValue("HP 'measured'", "HP", "p",
          (bytes) => (torque * rpm / 9549 * kw_to_hp));*/

      packets.Add(0x2E5, p = new Packet(0x2E5, this));
      p.AddValue("Fr mech power", " kW", "f", (bytes) => fMechPower =
          ((bytes[2] + ((bytes[3] & 0x7) << 8)) - (512 * (bytes[3] & 0x4))) / 2.0);
      p.AddValue("Fr dissipation", " kW", "f", (bytes) => fDissipation = bytes[1] * 125.0 / 1000.0 - 0.5);
      p.AddValue("Fr input power", " kW", "", (bytes) => fInput = fMechPower + fDissipation);
      p.AddValue("Fr mech power HP", "HP", "pf", (bytes) => fMechPower * kw_to_hp);
      p.AddValue("Fr stator current", "A", "f", (bytes) => bytes[4] + ((bytes[5] & 0x7) << 8));
      p.AddValue("Fr drive power max", " kW", "b", (bytes) => drivePowerMax =
          (((bytes[6] & 0x3F) << 5) + ((bytes[5] & 0xF0) >> 3)) + 1);
      p.AddValue("Mech power combined", " kW", "f", (bytes) => combinedMechPower = mechPower + fMechPower,
        new int[] { 0x266 });
      p.AddValue("HP combined", "HP", "pf", (bytes) => (mechPower + fMechPower) * kw_to_hp,
        new int[] { 0x266 });
      p.AddValue("Fr efficiency", "%", "e", (bytes) => fDissipation > 0.0 ? Math.Abs(fMechPower) / (Math.Abs(fMechPower) + fDissipation + 0.5) * 100.0 : (double?)null);
      //p.AddValue("Fr+Rr efficiency", "%", "e", (bytes) => Math.Abs(mechPower+fMechPower) / (Math.Abs(mechPower+fMechPower) + fDissipation + rDissipation) * 100.0);

      packets.Add(0x266, p = new Packet(0x266, this));
      p.AddValue("Rr inverter 12V", "V12", "", (bytes) => bytes[0] / 10.0);
      p.AddValue("Rr mech power", " kW", "", (bytes) => mechPower =
          ((bytes[2] + ((bytes[3] & 0x7) << 8)) - (512 * (bytes[3] & 0x4))) / 2.0);
      p.AddValue("Rr dissipation", " kW", "", (bytes) => {
        rDissipation = bytes[1] * 125.0 / 1000.0 - 0.5;
        dissipationUpdated = true;
        //dissipationTimeStamp = Android.OS.SystemClock.ElapsedRealtime();
        return rDissipation;
        });
      p.AddValue("Rr input power", " kW", "", (bytes) => rInput = mechPower + rDissipation);
      p.AddValue("Propulsion", " kW", "e", (bytes) => rInput + fInput);
      p.AddValue("Rr mech power HP", "HP", "p", (bytes) => mechPower * kw_to_hp);
      p.AddValue("Rr stator current", "A", "", (bytes) => bytes[4] + ((bytes[5] & 0x7) << 8));
      p.AddValue("Rr regen power max", "KW", "b", (bytes) => (bytes[7] * 4) - 200);
      p.AddValue("Rr drive power max", "KW", "b", (bytes) => drivePowerMax =
          (((bytes[6] & 0x3F) << 5) + ((bytes[5] & 0xF0) >> 3)) + 1);
      p.AddValue("Rr efficiency", "%", "e", (bytes) => rDissipation>0.0 ? Math.Abs(mechPower) / (Math.Abs(mechPower) + rDissipation + 0.5) * 100.0 : (double?)null);
      //p.AddValue("Non-propulsive", "kW", "e", (bytes) => power - (rInput+fInput));
      /*p.AddValue("Car efficiency", "%", "e", (bytes) => {
        //if (Math.Abs(mechPower + fMechPower) > Math.Abs(power))
          //return 100.0;
        //if Math.Abs(mechPower + fMechPower) < Math.Abs(power)
          //return 0.0;*/
        /*return Math.Abs(mechPower + fMechPower) / Math.Abs(power) * 100.0;
      });*/

      packets.Add(0x145, p = new Packet(0x145, this));
      p.AddValue("Fr torque estimate", "Nm", "f",
          (bytes) => ((bytes[0] + ((bytes[1] & 0xF) << 8)) - (512 * (bytes[1] & 0x8))) / 2);

      packets.Add(0x116, p = new Packet(0x116, this));
        p.AddValue("Rr torque estimate", "Nm", "",
            (bytes) => ((bytes[0] + ((bytes[1] & 0xF) << 8)) - (512 * (bytes[1] & 0x8))) / 2);
      p.AddValue("Speed", "km|h", "pe", (bytes) => speed = ((bytes[2] + ((bytes[3] & 0xF) << 8)) - 500) / 20.0 * miles_to_km);
      p.AddValue("Consumption", "wh|km", "p",
            (bytes) => power / speed * 1000,
            new int[] { 0x102 });

      packets.Add(0x306, p = new Packet(0x306, this));
      p.AddValue("Rr coolant inlet", "  C", "", (bytes) => bytes[5] == 0 ? (double?)null : bytes[5] - 40);
      p.AddValue("Rr inverter PCB", "  C", "", (bytes) => bytes[0] - 40);
      p.AddValue("Rr stator", "  C", "cp", (bytes) => bytes[2] - 40);
      p.AddValue("Rr DC capacitor", "  C", "", (bytes) => bytes[3] - 40);
      p.AddValue("Rr heat sink", "  C", "", (bytes) => bytes[4] - 40);
      p.AddValue("Rr inverter", "  C", "", (bytes) => bytes[1] - 40);

      packets.Add(0x382, p = new Packet(0x382, this));
      p.AddValue("Nominal full pack", "kWh", "br", (bytes) => nominalFullPackEnergy = (bytes[0] + ((bytes[1] & 0x03) << 8)) * 0.1);
      p.AddValue("Nominal remaining", "kWh", "br", (bytes) => nominalRemaining = ((bytes[1] >> 2) + ((bytes[2] & 0x0F) * 64)) * 0.1);
      p.AddValue("Expected remaining", "kWh", "r", (bytes) => ((bytes[2] >> 4) + ((bytes[3] & 0x3F) * 16)) * 0.1);
      p.AddValue("Ideal remaining", "kWh", "r", (bytes) => ((bytes[3] >> 6) + ((bytes[4] & 0xFF) * 4)) * 0.1);
      p.AddValue("To charge complete", "kWh", "", (bytes) => (bytes[5] + ((bytes[6] & 0x03) << 8)) * 0.1);
      p.AddValue("Energy buffer", "kWh", "br", (bytes) => buffer = ((bytes[6] >> 2) + ((bytes[7] & 0x03) * 64)) * 0.1);
      //p.AddValue("SOC nominal", "%", "br", (bytes) => nominalRemaining / nominalFullPackEnergy * 100.0);
      p.AddValue("SOC", "%", "br", (bytes) => soc = (nominalRemaining - buffer) / (nominalFullPackEnergy - buffer) * 100.0);
      p.AddValue("Usable full pack", "kWh", "br", (bytes) => (nominalFullPackEnergy-buffer));
      p.AddValue("Usable remaining", "kWh", "br", (bytes) => (nominalRemaining-buffer));

      packets.Add(0x302, p = new Packet(0x302, this));
      p.AddValue("SOC Min", "%", "br", (bytes) => (bytes[0] + ((bytes[1] & 0x3) << 8)) / 10.0);
      p.AddValue("SOC UI", "%", "br", (bytes) => ((bytes[1] >> 2) + ((bytes[2] & 0xF) << 6)) / 10.0);

      p.AddValue("DC Charge total", "kWH", "bs",
            (bytes) => {
              if (bytes[2] >> 4 == 0) {
                dcChargeTotal =
                  (bytes[4] +
                  (bytes[5] << 8) +
                  (bytes[6] << 16) +
                  (bytes[7] << 24)) / 1000.0;
                /*if (mainActivity.currentTab.trip.dcChargeStart == 0)
                  mainActivity.currentTab.trip.dcChargeStart = dcChargeTotal;
                dcCharge = dcChargeTotal - mainActivity.currentTab.trip.dcChargeStart;*/
                return dcChargeTotal;
              } else return (double?)null;
            });

      p.AddValue("AC Charge total", "kWH", "bs",
        (bytes) => {
          if (bytes[2] >> 4 == 1) {
            acChargeTotal =
              (bytes[4] +
              (bytes[5] << 8) +
              (bytes[6] << 16) +
              (bytes[7] << 24)) / 1000.0;
            /*if (mainActivity.currentTab.trip.acChargeStart == 0)
              mainActivity.currentTab.trip.acChargeStart = acChargeTotal;
            acCharge = acChargeTotal - mainActivity.currentTab.trip.acChargeStart;*/
            return acChargeTotal;
          } else return (double?)null;
        });
      /*p.AddValue("DC Charge", "kWh", "ti",
        (bytes) => dcChargeTotal - mainActivity.currentTab.trip.dcChargeStart);
      p.AddValue("AC Charge", "kWh", "ti",
        (bytes) => acChargeTotal - mainActivity.currentTab.trip.acChargeStart);-*/

      packets.Add(0x3D2, p = new Packet(0x3D2, this));
      p.AddValue("Charge total", "kWH", "bs",
                (bytes) => {
                  chargeTotal =
                    (bytes[0] +
                    (bytes[1] << 8) +
                    (bytes[2] << 16) +
                    (bytes[3] << 24)) / 1000.0;
                  /*if (mainActivity.currentTab.trip.chargeStart == 0)
                    mainActivity.currentTab.trip.chargeStart = chargeTotal;
                  charge = chargeTotal - mainActivity.currentTab.trip.chargeStart;*/
                  return chargeTotal;
                });

      p.AddValue("Discharge total", "kWH", "b",
          (bytes) => {
            dischargeTotal =
              (bytes[4] +
              (bytes[5] << 8) +
              (bytes[6] << 16) +
              (bytes[7] << 24)) / 1000.0;
            /*if (mainActivity.currentTab.trip.dischargeStart == 0)
              mainActivity.currentTab.trip.dischargeStart = dischargeTotal;
            discharge = dischargeTotal - mainActivity.currentTab.trip.dischargeStart;*/
            return dischargeTotal;
          });
      p.AddValue("Regenerated", "kWh", "tr",
          (bytes) => regen = charge - acCharge - dcCharge);
      p.AddValue("Energy", "kWh", "tr",
          (bytes) => energy = discharge - regen);
      p.AddValue("Discharge", "kWh", "r",
          (bytes) => discharge);
      p.AddValue("Charge", "kWh", "r",
          (bytes) => charge);
      p.AddValue("Regen total", "kWH", "b",
        (bytes) => regenTotal = chargeTotal - acChargeTotal - dcChargeTotal,
        new int[] { 0x302 });
      p.AddValue("Regen %", "% ", "tr",
          (bytes) => energy > 0 ? regen / discharge * 100 : (double?)null);//,
                  //new int[] { 0x302 });

      p.AddValue("Discharge cycles", "x", "b",
          (bytes) => nominalFullPackEnergy>0 ? dischargeTotal / nominalFullPackEnergy : (double?)null, 
          new int[] { 0x382 });
      p.AddValue("Charge cycles", "x", "b",
          (bytes) => nominalFullPackEnergy>0 ? chargeTotal / nominalFullPackEnergy : (double?)null, 
          new int[] { 0x382 });

      packets.Add(0x5D8, p = new Packet(0x5D8, this));
      p.AddValue("Odometer", "Km", "b",
          (bytes) => odometer = (bytes[0] + (bytes[1] << 8) + (bytes[2] << 16) + (bytes[3] << 24)) / 1000.0 * miles_to_km);
      /*p.AddValue("Distance", "km", "tsr",
          (bytes) => {
            if (mainActivity.currentTab.trip.odometerStart == 0)
              mainActivity.currentTab.trip.odometerStart = odometer;
            return tripDistance = odometer - mainActivity.currentTab.trip.odometerStart;
          });*/
      p.AddValue("Trip consumption", "wh|km", "tr",
          (bytes) => tripDistance > 0 ? energy / tripDistance * 1000 : (double?)null,
          new int[] { 0x3D2 });
      /*p.AddValue("Lifetime consumption", "wh/km", "bt",
          (bytes) => odometer > 0 ? dischargeTotal / odometer * 1000 : bytes[100]);*/

      packets.Add(0x562, p = new Packet(0x562, this));
      p.AddValue("Odometer (legacy)", "Km", "b",
          (bytes) => (bytes[0] + (bytes[1] << 8) + (bytes[2] << 16) + (bytes[3] << 24)) / 1000.0 * miles_to_km);


      packets.Add(0x115, p = new Packet(0x115, this));
      p.AddValue("Fr motor RPM", "RPM", "",
          (bytes) => frpm = (bytes[4] + (bytes[5] << 8)) - (512 * (bytes[5] & 0x80)));
      // 0x115 --- DIS_motorRPM = (data[4] + (data[5]<<8)) - (512 * (data[5]&0x80));

      packets.Add(0x106, p = new Packet(0x106, this));
      p.AddValue("Rr motor RPM", "RPM", "",
          (bytes) => rrpm = (bytes[4] + (bytes[5] << 8)) - (512 * (bytes[5] & 0x80)));

      packets.Add(0x232, p = new Packet(0x232, this));
      p.AddValue("BMS max discharge", "KW", "b", (bytes) => (bytes[2] + (bytes[3] << 8)) / 100.0);
      p.AddValue("BMS max charge", "KW", "b", (bytes) => (bytes[0] + (bytes[1] << 8)) / 100.0);

      packets.Add(0x168, p = new Packet(0x168, this));
      p.AddValue("Brake pedal", "%", "",
          (bytes) => (bytes[0] + (bytes[1] << 8)) - 3239);

      packets.Add(0x00E, p = new Packet(0x00E, this));
      p.AddValue("Steering angle", "deg", "",
        (bytes) => (((bytes[0] << 8) + bytes[1] - 8200.0) / 10.0));

      packets.Add(0x338, p = new Packet(0x338, this));
      p.AddValue("Rated range", "km", "br",
        (bytes) => (bytes[0] + (bytes[1] << 8)) * miles_to_km);
      p.AddValue("Typical range", "km", "br",
        (bytes) => (bytes[2] + (bytes[3] << 8)) * miles_to_km);
      p.AddValue("Full rated range", "km", "br",
        (bytes) => (bytes[0] + (bytes[1] << 8)) * miles_to_km / (soc == 0.0 ? 100.0 : soc) * 100.0);
      p.AddValue("Full typical range", "km", "br",
        (bytes) => (bytes[2] + (bytes[3] << 8)) * miles_to_km / (soc == 0.0 ? 100.0 : soc) * 100.0);



      packets.Add(0x6F2, p = new Packet(0x6F2, this));
      p.AddValue("Last cell block updated", "xb", "", (bytes) => {
        Int64 data = BitConverter.ToInt64(bytes, 0);
        if (bytes[0] < 24) {
          int cell = 0;
          for (int i = 0; i < 4; i++) {
            var val = ((data >> ((14 * i) + 8)) & 0x3FFF);
            if (val != 16383)
              UpdateItem("Cell " + (cell = ((bytes[0]) * 4 + i + 1)).ToString().PadLeft(2) + " voltage"
                , "zVC"
                , "z"
                , (bytes[0]) * 4 + i + 2000
                , val * 0.000305
                , 0x6F2);
          }
          if (cell > numCells)
            numCells = cell;
        } else
          for (int i = 0; i < 4; i++)
            UpdateItem("Cell " + ((bytes[0] - 24) * 4 + i + 1).ToString().PadLeft(2) + " temp"
              , "zCC"
              , "c"
              , ((bytes[0]-24) * 4 + i ) * 4 + 3 + 2000
              , ((Int16)(((data >> ((14 * i) + 6)) & 0xFFFC)) * 0.0122 / 4.0)
              , 0x6F2);

        return bytes[0];
      });

      // these are a bit stupid, but they are placeholders for the filters to be generated correctly.
      p.AddValue("Cell temp min", "C", "bcz", null, null, 1001);
      p.AddValue("Cell temp avg", "C", "bcpz", null, null, 1002);
      p.AddValue("Cell temp max", "C", "bcz", null, null, 1003);
      p.AddValue("Cell temp diff", "Cd", "bz", null, null, 1004);
      p.AddValue("Cell min", "Vc", "bz", null, null, 1005);
      p.AddValue("Cell avg", "Vc", "brzx", null, null, 1006);
      p.AddValue("Cell max", "Vc", "bz", null, null, 1007);
      p.AddValue("Cell diff", "Vcd", "bzx", null, null, 1008);
      for (int i = 1; i <= 96; i++)
        p.AddValue("Cell " + i.ToString().PadLeft(2) + " voltage"
          , "zVC"
          , "zx"
          , null 
          , null
          , i + 2000);
      for (int i = 1; i <= 32; i++)
        p.AddValue("Cell " + i.ToString().PadLeft(2) + " temp"
          , "zCC"
          , "z"
          , null
          , null
          , i*3 + 2000);


      /*packets.Add(0x222, p = new Packet(0x222, this));
      p.AddValue("Charge rate", "??", "e",
        (bytes) => (bytes[0] + (bytes[1] << 8)) / 100.0);
      p.AddValue("Charger volt", "V", "e",
        (bytes) => (bytes[2] + (bytes[3] << 8)) / 100.0);*/


      packets.Add(0x2A8, p = new Packet(0x2A8, this));
      p.AddValue("Front left", "WRPM", "",
        (bytes) => fl = (bytes[4] + (bytes[3] << 8)) * 0.7371875 / 9.73);
      p.AddValue("Front right", "WRPM", "",
        (bytes) => fr = (bytes[6] + (bytes[5] << 8)) * 0.7371875 / 9.73);
      p.AddValue("Front drive ratio", ":1", "",
        (bytes) => frpm > 1000 ? frpm / ((fl + fr) / 2) : (double?)null,
        new int[] { 0x115 });


      packets.Add(0x288, p = new Packet(0x288, this));
      p.AddValue("Rear left", "WRPM", "",
        (bytes) => rl = (bytes[4] + (bytes[3] << 8)) * 0.7371875 / 9.73);
      p.AddValue("Rear right", "WRPM", "",
        (bytes) => rr = (bytes[7] + (bytes[6] << 8)) * 0.7371875 / 9.73);
      p.AddValue("Rear drive ratio", ":1", "",
        (bytes) => rrpm > 1000 ? rrpm / ((rl + rr) / 2) : (double?)null,
        new int[] { 0x106 });

      packets.Add(0x3AA, p = new Packet(0x3AA, this));
      p.AddValue("Series/Parallel", "%", "bc",
        (bytes) => (bytes[0] & 0x80) == 0x80 ? 0 : 100);

      packets.Add(0x32A, p = new Packet(0x32A, this));
      p.AddValue("Battery pump 1", "%", "bc",
          (bytes) => (bytes[0]) & 0x7F);
      p.AddValue("Battery pump 2", "%", "bc",
          (bytes) => (bytes[1]));
      p.AddValue("Powertrain pump", "%", "bc",
          (bytes) => (bytes[2]));
      p.AddValue("Powertrain pump 2", "%", "bc",
          (bytes) => (bytes[7] > 0 ? bytes[7] : (double?)null));
      p.AddValue("Radiator bypass", "%", "bc",
          (bytes) => (bytes[3]));
      p.AddValue("Chiller bypass", "%", "bc",
          (bytes) => (bytes[4]));
      p.AddValue("Coolant heater", "%", "bch",
          (bytes) => (bytes[5]));
      p.AddValue("PTC air heater", "%", "bch",
          (bytes) => (bytes[6]));
      /*p.AddValue("Enabled?", "%", "bc",
          (bytes) => ((bytes[0] & 0x80) >> 7) * 100);*/

      packets.Add(0x318, p = new Packet(0x318, this));
      p.AddValue("Outside temp", " C", "h",
        (bytes) => (bytes[0] / 2.0 - 40));
      p.AddValue("Outside temp filtered", " C", "",
        (bytes) => (bytes[1] / 2.0 - 40));
      p.AddValue("Inside temp", " C", "h",
        (bytes) => (bytes[2] / 2.0 - 40));
      p.AddValue("A/C air temp", " C", "h",
        (bytes) => (bytes[4] / 2.0 - 40));

      packets.Add(0x33A, p = new Packet(0x33A, this));
      p.AddValue("Refrigerant temp", " C", "bch",
        (bytes) => (bytes[0] + ((bytes[1] & 0x07) << 8) - 320) / 8.0);

      packets.Add(0x388, p = new Packet(0x388, this));
      p.AddValue("Heater L", " C", "h",
        (bytes) => (bytes[1] - 40));
      p.AddValue("Heater R", " C", "h",
        (bytes) => (bytes[0] - 40));

      packets.Add(0x3F8, p = new Packet(0x3F8, this));
      p.AddValue("Mid vent L", " C", "h",
        (bytes) => ((bytes[0] + (bytes[1] << 8)) / 10.0) - 40);
      p.AddValue("Mid vent R", " C", "h",
        (bytes) => ((bytes[2] + (bytes[3] << 8)) / 10.0) - 40);
      p.AddValue("Floor vent L", " C", "h",
        (bytes) => ((bytes[4] + (bytes[5] << 8)) / 10.0) - 40);
      p.AddValue("Floor vent R", " C", "h",
        (bytes) => ((bytes[6] + (bytes[7] << 8)) / 10.0) - 40);
      //3F8 - as int. tror dette er 4 tempavlesninger evt innblåstemperatur, F / 10->C

      packets.Add(0x308, p = new Packet(0x308, this));
      p.AddValue("Louver 1", "%", "h",
        (bytes) => bytes[0] > 0 ? ((bytes[0] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 2", "%", "h",
        (bytes) => bytes[1] > 0 ? ((bytes[1] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 3", "%", "h",
        (bytes) => bytes[2] > 0 ? ((bytes[2] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 4", "%", "h",
        (bytes) => bytes[3] > 0 ? ((bytes[3] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 5", "%", "h",
        (bytes) => bytes[4] > 0 ? ((bytes[4] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 6", "%", "h",
        (bytes) => bytes[5] > 0 ? ((bytes[5] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 7", "%", "h",
        (bytes) => bytes[6] > 0 ? ((bytes[6] - 15.0) / 219.0) * 100.0 : (double?)null);
      p.AddValue("Louver 8", "%", "h",
        (bytes) => bytes[7] > 0 ? ((bytes[7] - 15.0) / 219.0) * 100.0 : (double?)null);


      packets.Add(0x2AA, p = new Packet(0x2AA, this));
      p.AddValue("HVAC floor", "0", "h",
          (bytes) => {
            var set1 = bytes[2] & 0x07;
            feet = false;
            seat = false;
            win = false;
            switch (set1) {
              case 1:
                seat = true;
                break;
              case 2:
                feet = true;
                seat = true;
                break;
              case 3:
                feet = true;
                break;
              case 4:
                feet = true;
                win = true;
                break;
              case 5:
                win = true;
                break;
              case 6:
                feet = true;
                seat = true;
                win = true;
                break;
              case 7:
                seat = true;
                win = true;
                break;
            }
            return feet ? 1 : 0;
          });
      p.AddValue("HVAC mid", "0", "h",
          (bytes) => seat ? 1 : 0);
      p.AddValue("HVAC window", "0", "h",
          (bytes) => win ? 1 : 0);

      /*p.AddValue("HVAC recycle", "0", "eh",
          (bytes) => {
            return (bytes[3] & 0x10) >> 4;
          });
      p.AddValue("HVAC recycle2", "0", "eh",
          (bytes) => {
            return (bytes[3] & 0x8) >> 3;
          });*/
      p.AddValue("HVAC A/C", "0", "h",
          (bytes) => {
            var set3 = bytes[4] & 0x01;
            return set3;
          });
      p.AddValue("HVAC on/off", "0", "h",
          (bytes) =>
             (bytes[3] & 0x10) >> 4 == 0 ? 1 : 0);

      p.AddValue("HVAC fan speed", "X", "h",
          (bytes) => (bytes[2] & 0xf0) >> 4);

      p.AddValue("HVAC temp left", " C", "h",
          (bytes) => bytes[0] / 2.0);
      p.AddValue("HVAC temp right", " C", "h",
          (bytes) => bytes[1] / 2.0);
    }



    private void ParsePacket(string raw, int id, byte[] bytes) {
      if (packets.ContainsKey(id)) {
        packets[id].Update(bytes);
      }
    }

    public void UpdateItem(string name, string unit, string tag, int index, double value, int id) {

      if (!Double.IsInfinity(value) && !Double.IsNaN(value)) { // influxDB does not support Infinity. Let's not waste time by getting that error back
        program.SendToDBAsync(name, value, program.file, program.timestamp).Wait() ;
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
          if ((!(line.Length == 9 && (line.StartsWith("26A")))) &&
              (!(line.Length == 11 && (line.StartsWith("5D8") || line.StartsWith("562") || line.StartsWith("232") || line.StartsWith("26A")))) &&
              (!(line.Length == 15 && (line.StartsWith("116") || line.StartsWith("222")))) &&
              (!(line.Length == 17 && (line.StartsWith("210") || line.StartsWith("115")))) &&
               line.Length != 19) { // testing an aggressive garbage collector! // 11)
#if VERBOSE
            Console.WriteLine("GC " + line);
#endif
            continue;
          }
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
        } catch (Exception e) { Console.WriteLine(e.ToString()); };

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

