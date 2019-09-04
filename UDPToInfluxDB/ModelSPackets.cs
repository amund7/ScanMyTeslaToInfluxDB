using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeslaSCAN;
using UDPToInfluxDB;

namespace CANBUS {
  class ModelSPackets : Parser {

    double nominalFullPackEnergy;
    double amp;
    double volt;
    double power;
    double mechPower;
    double fMechPower;
    double speed;
    private long speedDTnanos;
    private double speedDT;
    double drivePowerMax;
    double torque;
    double chargeTotal;
    double dischargeTotal;
    double odometer;
    double tripDistance;
    double charge;
    double discharge;
    bool metric = true;
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
    private double dcOut;
    private double dcIn;
    private double rInput;
    private double fInput;
    private double fDissipation;
    private double combinedMechPower;
    private double rDissipation;
    private double hvacPower;
    private bool dissipationUpdated;
    private long dissipationTimeStamp;
    private int statorTemp;
    private int inverterTemp;
    int numCells;
    private double thc_hv;
    private double thc_lv;
    private double ampSum;
    private int ampCount;
    private int ampOffset;
    private long speedTimeStamp;
    private double speedTimingAccuracy;

    public ModelSPackets(Program prog) : base(prog) {

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

      //speedTimeStamp = SystemClock.ElapsedRealtimeNanos();

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
      p.AddValue("Battery voltage", " V", "Sbpr", (bytes) => volt =
          (bytes[0] + (bytes[1] << 8)) / 100.0);
      /*p.AddValue("Battery current", " A", "Sb", (bytes) => amp =
          1000 - ((Int16)((((bytes[3] & 0x7F) << 8) + bytes[2]) << 1)) / 20.0);
      p.AddValue("Battery power", " kW", "Sbpe", (bytes) => power = amp * volt / 1000.0);*/
      p.AddValue("Battery current", " A", "Sb", (bytes) => {
        amp =
          ((Int16)((((bytes[3] & 0x7F) << 8) + bytes[2]) << 1)) / 20.0;
        //((Int16)((((bytes[3]) << 8) + bytes[2]))) / 10.0;
        ampSum += amp;
        ampCount++;
        if (ampCount < 100) {
          if (ampSum / ampCount > 650)
            ampOffset = 1000;
          else
            ampOffset = 0;
        } else
          return amp = ampOffset - amp;
        return null;
      });

      p.AddValue("Battery power", " kW", "Sbpe", (bytes) => {
        if (ampCount > 100)
          return power = amp * volt / 1000.0;
        else
          return null;
      });
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
      p.AddValue("Battery heater temp", "C", "e",
        (bytes) => ((bytes[0] + ((bytes[1] & 0x7) << 8)) * 0.125) - 40);
      /*p.AddValue("Battery heater req", "b", "e",
        (bytes) => (bytes[1] & 0x8) >> 3);
      p.AddValue("Battery heater state", "b", "e",
        (bytes) => (bytes[2] & 0x70) >> 4);*/
      //(bytes) => (bytes[0] *0.4 ));
      //(bytes) => (((bytes[1] & 0xF0) >> 4) + ((bytes[2]) << 8)));
      /*
       * #THC human decode
        THC_batteryHeaterState = "Undefined"
        heater_state = []
        heater_state.append("Off")
        heater_state.append("Startup")
        heater_state.append("BAT_IN_HEAT_CK")
        heater_state.append("Run")
        heater_state.append("Overtemp")
        heater_state.append("Suspended")
        heater_state.append("Undefined")
        heater_state.append("Undefined")
       
      THC (thermal controller) found at 
      https://github.com/apach3guy/CAN3/blob/master/thc.py
       
       */


      /*packets.Add(0x26A, p = new Packet(0x26A, this));
      p.AddValue("Coolant heater exit", "C", "c",
        (bytes) => (bytes[0] + ((bytes[1] & 0x03) << 8) - 320) / 8.0);*/

      packets.Add(0x35A, p = new Packet(0x35A, this));
      p.AddValue("Thermal controller 400V", "kW", "e",
        (bytes) => thc_hv = (bytes[2] + (bytes[3] << 8)) / 1000.0);
      p.AddValue("Thermal controller 12V", "kW", "e",
        (bytes) => thc_lv = (bytes[4] + ((bytes[5] & 0xF) << 8)) / 1000.0);
      p.AddValue("Thermal controller", "kW", "ech",
        (bytes) => thc_lv + thc_hv);
      /*p.AddValue("Heating/cooling", "kW", "eh",
        (bytes) => hvacPower = thc_hv + thc_lv);*/

      packets.Add(0x210, p = new Packet(0x210, this));
      p.AddValue("DC-DC current", "A12", "b", (bytes) => bytes[4]);
      p.AddValue("DC-DC voltage", "V12", "b", (bytes) => bytes[5] / 10.0);
      p.AddValue("DC-DC coolant inlet", "  C", "b", (bytes) => ((bytes[2] - (2 * (bytes[2] & 0x80))) * 0.5) + 40);
      p.AddValue("DC-DC input power", "W", "b", (bytes) => dcIn = (bytes[3] * 16));
      p.AddValue("12v systems", "W", "e", (bytes) => dcIn = (bytes[3] * 16));
      p.AddValue("DC-DC output power", "W", "b", (bytes) => dcOut = (bytes[4] * bytes[5] / 10.0));
      p.AddValue("DC-DC efficiency", "%", "e", (bytes) => dcOut / dcIn * 100.0);
      p.AddValue("400V systems", " kW", "e", (bytes) => power - dcIn / 1000.0);

      packets.Add(0x1D4, p = new Packet(0x1D4, this));
      p.AddValue("Fr torque measured", "Nm", "pf", (bytes) => frTorque =
         (bytes[5] + ((bytes[6] & 0x1F) << 8) - (512 * (bytes[6] & 0x10))) * 0.25);
      p.AddValue("Rr/Fr torque bias", "%", "pf",
        (bytes) => Math.Abs(frTorque) + Math.Abs(torque) == 0 ? 50 : Math.Abs(torque) / (Math.Abs(frTorque) + Math.Abs(torque)) * 100);

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
        //dissipationTimeStamp = SystemClock.ElapsedRealtime();
        return rDissipation;
      });
      p.AddValue("Rr input power", " kW", "", (bytes) => rInput = mechPower + rDissipation);
      p.AddValue("Propulsion", " kW", "e", (bytes) => rInput + fInput);
      p.AddValue("Rr mech power HP", "HP", "p", (bytes) => mechPower * kw_to_hp);
      p.AddValue("Rr stator current", "A", "", (bytes) => bytes[4] + ((bytes[5] & 0x7) << 8));
      p.AddValue("Rr regen power max", "KW", "b", (bytes) => (bytes[7] * 4) - 200);
      p.AddValue("Rr drive power max", "KW", "b", (bytes) => drivePowerMax =
          (((bytes[6] & 0x3F) << 5) + ((bytes[5] & 0xF0) >> 3)) + 1);
      p.AddValue("Rr efficiency", "%", "e", (bytes) => rDissipation > 0.0 ? Math.Abs(mechPower) / (Math.Abs(mechPower) + rDissipation + 0.5) * 100.0 : (double?)null);
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
      p.AddValue("Speed", "km|h", "pem", (bytes) => {
        speed = ((bytes[2] + ((bytes[3] & 0xF) << 8)) - 500) / 20.0 * miles_to_km;
        //speedDTnanos = -speedTimeStamp + (speedTimeStamp = SystemClock.ElapsedRealtimeNanos());
        //speedDT = speedDTnanos / 1000000000.0;
        if (speed == null)
          return null;
        if (speed < 0.001 && speed > -0.001)
          speed = 0;
        /*mainActivity.speedTimer.Traverse(this,
          mainActivity.convertToImperial ?
          (double)speed / miles_to_km :
          (double)speed);*/
        return speed;
      }, null, 1500);

      /*p.AddValue("Accuracy", "sec", "pm", (bytes) => {
        if (speedDT > speedTimingAccuracy)
          speedTimingAccuracy = speedDT;
        else
          speedTimingAccuracy = speedTimingAccuracy * 0.99 + speedDT * 0.01;
        return speedTimingAccuracy;
      });*/

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
      p.AddValue("SOC", "%", "brm", (bytes) => soc = (nominalRemaining - buffer) / (nominalFullPackEnergy - buffer) * 100.0);
      p.AddValue("Usable full pack", "kWh", "br", (bytes) => (nominalFullPackEnergy - buffer));
      p.AddValue("Usable remaining", "kWh", "br", (bytes) => (nominalRemaining - buffer));

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
        (bytes) => acChargeTotal - mainActivity.currentTab.trip.acChargeStart);*/

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
          (bytes) => nominalFullPackEnergy > 0 ? dischargeTotal / nominalFullPackEnergy : (double?)null,
          new int[] { 0x382 });
      p.AddValue("Charge cycles", "x", "b",
          (bytes) => nominalFullPackEnergy > 0 ? chargeTotal / nominalFullPackEnergy : (double?)null,
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
              , ((bytes[0] - 24) * 4 + i) * 4 + 3 + 2000
              , ((Int16)(((data >> ((14 * i) + 6)) & 0xFFFC)) * 0.0122 / 4.0)
              , 0x6F2);

        return bytes[0];
      });

      // these are a bit stupid, but they are placeholders for the filters to be generated correctly.
      p.AddValue("Cell temp min", "C", "bcz", null, null, 1001);
      p.AddValue("Cell temp avg", "C", "bcpzm", null, null, 1002);
      p.AddValue("Cell temp max", "C", "bcz", null, null, 1003);
      p.AddValue("Cell temp diff", "Cd", "bz", null, null, 1004);
      p.AddValue("Cell min", "Vc", "bz", null, null, 1005);
      p.AddValue("Cell avg", "Vc", "brzx", null, null, 1006);
      p.AddValue("Cell max", "Vc", "bz", null, null, 1007);
      p.AddValue("Cell diff", "Vcd", "bzx", null, null, 1008);
      p.AddValue("Cell imbalance", "mV", "b", null, null, 1009);
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
          , i * 3 + 2000);


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


      /*p.AddValue("Thermal PowerLimit 400V", "kW", "th",
        (bytes) => (bytes[6] + (bytes[7] << 8)) / 10000.0);
      p.AddValue("THC_limitedBatteryHeater", "b", "th",
        (bytes) => (bytes[5] & 0x10) >> 4);
      p.AddValue("THC_limitedCompressor", "b", "th",
        (bytes) => (bytes[5] & 0x20) >> 5);
      p.AddValue("THC_limitedPtcHeater", "b", "th",
        (bytes) => (bytes[5] & 0x40) >> 6);*/

#if freeversion
      packets.Add(0x132, p = new Packet(0x132, this));
      p.AddValue("Battery voltage 3", " V", "3bpr", (bytes) => volt =
          (bytes[0] + (bytes[1] << 8)) / 100.0);
      p.AddValue("Battery current 3", " A", "3b", (bytes) => amp =
          1000 - ((Int16)((((bytes[3] & 0x7F) << 8) + bytes[2]) << 1)) / 20.0);
      p.AddValue("Battery power 3", " kW", "3bpe", (bytes) => power = amp * volt / 1000.0);
#endif

    }  
  }
}
