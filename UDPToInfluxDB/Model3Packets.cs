using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeslaSCAN;
using UDPToInfluxDB;

namespace CANBUS {
  class Model3Packets : Parser {
    private const double odometerInvalid = -0.001;
    private double mechPower;
    private double rDissipation;
    private double volt;
    private double amp;
    private double power;
    private double odometer;
    private double torque;
    private int numCells;
    private int rrpm;
    private int drivePowerMax;
    private double chargeTotal;
    private double dischargeTotal;
    private double nominalFullPackEnergy;
    private double nominalRemaining;
    private double buffer;
    private double soc;
    private double charge;
    private double discharge;
    private double tripDistance;
    private double? speed;
    private long speedDTnanos;
    private double speedDT;
    private long speedTimeStamp;
    private double ampOffset;
    private double avgAmp;
    private int ampCount;
    private double ampSum;
    private double? cellTempMax;
    private double? cellTempMin;
    private double speedTimingAccuracy;
    private double dcChargeTotal;
    private double dcCharge;
    private double acChargeTotal;
    private double acCharge;
    private double regen;
    private double regenTotal;
    private double driveTotal;
    private double drive;
    private double? cellVoltMax;
    private double? cellVoltMin;
    private double? maxChargeCurrent;

    public Model3Packets(Program prog) : base(prog) {

      /* tags:
        p: performance
        t: trip
        b: battery
        c: temperature
        f: front drive unit
        s: startup (app will wait until these packets are found before starting 'normal' mode)
        i: imperial
        //m: metric
        m: tiMing
        i: ignore
      */


      Packet p;

      //speedTimeStamp = SystemClock.ElapsedRealtimeNanos();

      packets.Add(0x132, p = new Packet(0x132, this));

      // this is a placeholder for the item to show up in the tabs
      p.AddValue("Packets per second", "C", "ipb", null, null, 3000);

      p.AddValue("Battery voltage", " V", "bpr", (bytes) => volt =
          (bytes[0] + (bytes[1] << 8)) / 100.0);

      p.AddValue("Battery current", " A", "b", (bytes) => {
        amp =
          ((Int16)((((bytes[3]) << 8) + bytes[2]))) / 10.0;
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

      p.AddValue("Battery power", " kW", "bp", (bytes) => {
        if (ampCount > 100)
          return power = amp * volt / 1000.0;
        else
          return null;
      });

      packets.Add(0x2E5, p = new Packet(0x2E5, this));
      p.AddValue("F power", "kW", "p", (bytes) =>
        ExtractSignalFromBytes(bytes, 16, 11, true, 0.5, 0));
      /*p.AddValue("F heat power", "kW", "e", (bytes) =>
        ExtractSignalFromBytes(bytes, 48, 8, true, 0.08, 0));*/

      packets.Add(0x266, p = new Packet(0x266, this));
      p.AddValue("R power", "kW", "p", (bytes) =>
        ExtractSignalFromBytes(bytes, 16, 11, true, 0.5, 0));
      /*p.AddValue("R heat power", "kW", "e", (bytes) =>
        ExtractSignalFromBytes(bytes, 48, 8, true, 0.08, 0));*/


      /*packets.Add(0x186, p = new Packet(0x186, this));
      p.AddValue("F torque", "Nm", "ph", (bytes) =>
        ExtractSignalFromBytes(bytes, 27, 13, true, 2, 0));*/
      packets.Add(0x1D4, p = new Packet(0x1D4, this));
      p.AddValue("F torque", "Nm", "p", (bytes) =>
        (bytes[5] + ((bytes[6] & 0x1F) << 8) - (512 * (bytes[6] & 0x10))) * 0.25);

      packets.Add(0x154, p = new Packet(0x154, this));
      p.AddValue("R torque", "Nm", "p", (bytes) =>
        (bytes[5] + ((bytes[6] & 0x1F) << 8) - (512 * (bytes[6] & 0x10))) * 0.25);

      packets.Add(0x108, p = new Packet(0x108, this));
      p.AddValue("R torque (108)", "Nm", "p", (bytes) =>
        ExtractSignalFromBytes(bytes, 27, 13, true, .22222, 0));

      /*packets.Add(0x154, p = new Packet(0x154, this));
      p.AddValue("Rr torque measured", "Nm", "p", (bytes) => torque =
         (bytes[5] + ((bytes[6] & 0x1F) << 8) - (512 * (bytes[6] & 0x10))) * 0.25);*/

      //packets.Add(0x108, p = new Packet(0x108, this));
      p.AddValue("Rr motor RPM", "RPM", "",
          (bytes) => rrpm = (bytes[5] + (bytes[6] << 8)) - (512 * (bytes[6] & 0x80)));

      packets.Add(0x376, p = new Packet(0x376, this));
      p.AddValue("Inverter PCB temp", "C", "c",
        (bytes) => (bytes[0] - 40));
      p.AddValue("Inverter temp", "C", "c",
        (bytes) => (bytes[1] - 40));
      p.AddValue("Stator temp", "C", "c",
        (bytes) => (bytes[2] - 40));
      p.AddValue("Inverter capacitor temp", "C", "c",
        (bytes) => (bytes[4] - 40));

      packets.Add(0x252, p = new Packet(0x252, this));
      p.AddValue("Max discharge power", "KW", "b", (bytes) => (bytes[2] + (bytes[3] << 8)) / 100.0);
      p.AddValue("Max regen power", "KW", "b", (bytes) => (bytes[0] + (bytes[1] << 8)) / 100.0);

      packets.Add(0x268, p = new Packet(0x268, this));
      p.AddValue("Sys max drive power", "kW", "b", (bytes) => (bytes[2]));
      p.AddValue("Sys max regen power", "kW", "b", (bytes) => (bytes[3]));
      p.AddValue("Sys max heat power", "kW", "b", (bytes) => (bytes[0] * 0.08));
      p.AddValue("Sys heat power", "kW", "b", (bytes) => (bytes[1] * 0.08));

      packets.Add(0x3FE, p = new Packet(0x3FE, this));
      p.AddValue("FL brake est", " C", "c", (bytes) =>
        ExtractSignalFromBytes(bytes, 0, 10, false, 1, -40));
      p.AddValue("FR brake est", " C", "c", (bytes) =>
        ExtractSignalFromBytes(bytes, 10, 10, false, 1, -40));
      p.AddValue("RL brake est", " C", "c", (bytes) =>
        ExtractSignalFromBytes(bytes, 20, 10, false, 1, -40));
      p.AddValue("RR brake est", " C", "c", (bytes) =>
        ExtractSignalFromBytes(bytes, 30, 10, false, 1, -40));

      packets.Add(0x257, p = new Packet(0x257, this));
      p.AddValue("Speed", "km|h", "pm", (bytes) => {
        speed = ExtractSignalFromBytes(bytes, 12, 12, false, 0.08, -40);
        /*speedDTnanos = -speedTimeStamp + (speedTimeStamp = SystemClock.ElapsedRealtimeNanos());
        speedDT = speedDTnanos / 1000000000.0;*/
        if (speed == null)
          return null;
        if (speed < 0.001 && speed > -0.001)
          speed = 0;
       /* mainActivity.speedTimer.Traverse(this,
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

      /*p.AddValue("Consumption", "wh|km", "p",
        (bytes) => power / speed * 1000,
        new int[] { 0x132 });*/

      /*
      BO_ 1010 BMS_kwhCountersMultiplexed: 8 GTW
      SG_ BMS_kwhCounter_Id M : 0 | 3@1 + (1, 0)[0 | 0] "" ETH
      SG_ BMS_acChargerKwhTotal m0: 8 | 32@1 + (0.001, 0)[0 | 0] "KWh" ETH
      SG_ BMS_dcChargerKwhTotal m1: 8 | 32@1 + (0.001, 0)[0 | 0] "KWh" ETH
      SG_ BMS_kwhRegenChargeTotal m2: 8 | 32@1 + (0.001, 0)[0 | 0] "KWh" ETH
      SG_ BMS_kwhDriveDischargeTotal m3: 8 | 32@1 + (0.001, 0)[0 | 0] "KWh" ETH
      */
      packets.Add(0x3F2, p = new Packet(0x3F2, this));
      p.AddValue("DC Charge total", "kWH", "b", (bytes) => {
        if ((bytes[0] & 7) == 1) {
          dcChargeTotal = (bytes[1] + (bytes[2] << 8) + (bytes[3] << 16) + (bytes[4] << 24)) * 0.001;
          /*if (mainActivity.currentTab.trip.dcChargeStart == 0) {
            mainActivity.currentTab.trip.dcChargeStart = dcChargeTotal;
            mainActivity.SaveTabs();
          }
          dcCharge = dcChargeTotal - mainActivity.currentTab.trip.dcChargeStart;*/
          return dcChargeTotal;
        } else return null;
      });
      p.AddValue("AC Charge total", "kWH", "b", (bytes) => {
        if ((bytes[0] & 7) == 0) {
          acChargeTotal = (bytes[1] + (bytes[2] << 8) + (bytes[3] << 16) + (bytes[4] << 24)) * 0.001;
          /*if (mainActivity.currentTab.trip.acChargeStart == 0) {
            mainActivity.currentTab.trip.acChargeStart = acChargeTotal;
            mainActivity.SaveTabs();
          }
          acCharge = acChargeTotal - mainActivity.currentTab.trip.acChargeStart;*/
          return acChargeTotal;
        } else return null;
      });
      /*p.AddValue("DC Charge", "kWh", "ti",
        (bytes) => dcChargeTotal - mainActivity.currentTab.trip.dcChargeStart);
      p.AddValue("AC Charge", "kWh", "ti",
        (bytes) => acChargeTotal - mainActivity.currentTab.trip.acChargeStart);*/

      p.AddValue("Regen total", "kWH", "b", (bytes) => {
        if ((bytes[0] & 7) == 2) {
          regenTotal = (bytes[1] + (bytes[2] << 8) + (bytes[3] << 16) + (bytes[4] << 24)) * 0.001;
          /*if (mainActivity.currentTab.trip.regenStart == 0) {
            mainActivity.currentTab.trip.regenStart = regenTotal;
            mainActivity.SaveTabs();
          }
          regen = regenTotal - mainActivity.currentTab.trip.regenStart;*/
          return regenTotal;
        } else return null;
      });
      p.AddValue("Drive total", "kWH", "b", (bytes) => {
        if ((bytes[0] & 7) == 3) {
          driveTotal = (bytes[1] + (bytes[2] << 8) + (bytes[3] << 16) + (bytes[4] << 24)) * 0.001 - regenTotal;
          /*if (mainActivity.currentTab.trip.driveStart == 0) {
            mainActivity.currentTab.trip.driveStart = driveTotal;
            mainActivity.SaveTabs();
          }
          drive = driveTotal - mainActivity.currentTab.trip.driveStart;*/
          return driveTotal;
        } else return null;
      });

      /*p.AddValue("Regenerated", "kWh", "tr",
        (bytes) => regen);*/
      /*p.AddValue("RegenFromCharge", "kWh", "tr",
          (bytes) => charge - acCharge - dcCharge);*/
      /*p.AddValue("Energy", "kWh", "tr",
          (bytes) => drive);*/
      /*p.AddValue("Energy2", "kWh", "tr",
          (bytes) => discharge - (charge - acCharge - dcCharge));*/
      /*p.AddValue("Discharge", "kWh", "r",
          (bytes) => discharge);
      p.AddValue("Charge", "kWh", "r",
          (bytes) => charge);*/
      /*p.AddValue("Regen %", "% ", "tr",
          (bytes) => drive > 0 ? regen / drive * 100 : (double?)null);*///,
      /*p.AddValue("Stationary", "kWh", "tr",
          (bytes) => ((discharge - (charge - acCharge - dcCharge)) - drive));*/


      packets.Add(0x3B6, p = new Packet(0x3B6, this));
      p.AddValue("Odometer", "Km", "b",
          (bytes) => {
            var temp = (bytes[0] + (bytes[1] << 8) + (bytes[2] << 16) + (bytes[3] << 24)) / 1000.0;
            if (temp != odometerInvalid)
              return odometer = (bytes[0] + (bytes[1] << 8) + (bytes[2] << 16) + (bytes[3] << 24)) / 1000.0;
            else return null;
          });

      /*p.AddValue("Distance", "km", "tr",
          (bytes) => {
            if (mainActivity.currentTab.trip.odometerStart == 0
                && odometer != odometerInvalid) {
              mainActivity.currentTab.trip.odometerStart = odometer;
              mainActivity.SaveTabs();
            }
            return tripDistance = odometer - mainActivity.currentTab.trip.odometerStart;
          });*/
      /*p.AddValue("Avg consumption", "wh|km", "tr",
          (bytes) => tripDistance > 0 ? drive / tripDistance * 1000 : (double?)null,
            new int[] { 1010 });*/


      packets.Add(0x352, p = new Packet(0x352, this));
      p.AddValue("Nominal full pack", "kWh", "br", (bytes) => nominalFullPackEnergy = (bytes[0] + ((bytes[1] & 0x03) << 8)) * 0.1);
      p.AddValue("Nominal remaining", "kWh", "br", (bytes) => nominalRemaining = ((bytes[1] >> 2) + ((bytes[2] & 0x0F) * 64)) * 0.1);
      p.AddValue("Expected remaining", "kWh", "r", (bytes) => ((bytes[2] >> 4) + ((bytes[3] & 0x3F) * 16)) * 0.1);
      p.AddValue("Ideal remaining", "kWh", "r", (bytes) => ((bytes[3] >> 6) + ((bytes[4] & 0xFF) * 4)) * 0.1);
      p.AddValue("To charge complete", "kWh", "", (bytes) => (bytes[5] + ((bytes[6] & 0x03) << 8)) * 0.1);
      p.AddValue("Energy buffer", "kWh", "br", (bytes) => buffer = ((bytes[6] >> 2) + ((bytes[7] & 0x03) * 64)) * 0.1);
      p.AddValue("SOC", "%", "brm", (bytes) => soc = (nominalRemaining - buffer) / (nominalFullPackEnergy - buffer) * 100.0);

      packets.Add(0x292, p = new Packet(0x292, this));
      p.AddValue("SOC UI", "%", "br", (bytes) => (bytes[0] + ((bytes[1] & 0x3) << 8)) / 10.0);
      p.AddValue("SOC Min", "%", "br", (bytes) => ((bytes[1] >> 2) + ((bytes[2] & 0xF) << 6)) / 10.0);
      p.AddValue("SOC Max", "%", "br", (bytes) => ((bytes[2] >> 4) + ((bytes[3] & 0x3F) << 4)) / 10.0);
      p.AddValue("SOC Avg", "%", "br", (bytes) => ((bytes[3] >> 6) + ((bytes[4]) << 2)) / 10.0);

      packets.Add(0x332, p = new Packet(0x332, this));
      p.AddValue("Cell temp max", "C", "cb", (bytes) => {
        if ((bytes[0] & 3) == 0)
          return cellTempMax = ExtractSignalFromBytes(bytes, 16, 8, false, 0.5, -40);
        else return null;
      });
      p.AddValue("Cell temp min", "C", "cb", (bytes) => {
        if ((bytes[0] & 3) == 0)
          return cellTempMin = ExtractSignalFromBytes(bytes, 24, 8, false, 0.5, -40);
        else return null;
      });
      p.AddValue("Cell temp mid", "C", "cbmp", (bytes) => {
        if ((bytes[0] & 3) == 0)
          return (cellTempMax + cellTempMin) / 2.0;
        else return null;
      });

      p.AddValue("Cell volt max", "Vcc", "b", (bytes) => {
        if ((bytes[0] & 3) == 1)
          return cellVoltMax = ExtractSignalFromBytes(bytes, 2, 12, false, 0.002, 0);
        else return null;
      });
      p.AddValue("Cell volt min", "Vcc", "b", (bytes) => {
        if ((bytes[0] & 3) == 1)
          return cellVoltMin = ExtractSignalFromBytes(bytes, 16, 12, false, 0.002, 0);
        else return null;
      });
      p.AddValue("Cell volt mid", "Vcc", "b", (bytes) => {
        if ((bytes[0] & 3) == 1)
          return (cellVoltMax + cellVoltMin) / 2.0;
        else return null;
      });
      p.AddValue("Cell imbalance", "mV", "b", (bytes) => {
        if ((bytes[0] & 3) == 1)
          return (cellVoltMax - cellVoltMin) * 1000;
        else return null;
      });



      /*SG_ BMS_thermistorTMin m0: 24 | 8@1 + (0.5, -40)[0 | 0] "DegC" X
           SG_ BMS_modelTMax m0: 32 | 8@1 + (0.5, -40)[0 | 0] "DegC" X
                SG_ BMS_modelTMin m0: 40 | 8@1 + (0.5, -40)[0 | 0] "DegC" X
                     SG_ BMS_brickVoltageMax m1: 2 | 12@1 + (0.002, 0)[0 | 0] "V" X
                          SG_ BMS_brickVoltageMin m1: 16 | 12@1 + (0.002, 0)[0 | 0] "V" X
                               SG_ BMS_brickNumVoltageMax m1: 32 | 7@1 + (1, 1)[0 | 0] "" X
                                    SG_ BMS_brickNumVoltageMin m1: 40 | 7@1 + (1, 1)[0 | 0] "" X*/




      packets.Add(0x212, p = new Packet(0x212, this));
      p.AddValue("Battery min temp", "C", "bc",
        (bytes) => ((bytes[7]) / 2.0) - 40.0);

      packets.Add(0x321, p = new Packet(0x321, this));
      p.AddValue("Battery inlet", "C", "c",
        (bytes) => ((bytes[0] + ((bytes[1] & 0x3) << 8)) * 0.125) - 40);
      p.AddValue("Powertrain inlet", "C", "c",
        (bytes) => (((((bytes[2]& 0xF)<<8) + bytes[1])>>2) * 0.125) - 40);
      p.AddValue("Outside temp", "C", "c",
        (bytes) => ((bytes[3] * 0.5) - 40));
      p.AddValue("Outside temp filtered", "C", "",
        (bytes) => ((bytes[5] * 0.5) - 40));

      packets.Add(0x241, p = new Packet(0x241, this));
      p.AddValue("Battery flow", "lpm", "c",
        (bytes) =>
        ExtractSignalFromBytes(bytes, 0, 9, false, 0.1, 0));
      p.AddValue("Powertrain flow", "lpm", "c",
        (bytes) =>
        ExtractSignalFromBytes(bytes, 22, 9, false, 0.1, 0));

      packets.Add(0x3D2, p = new Packet(0x3D2, this));
      p.AddValue("Charge total", "kWH", "b",
                (bytes) => {
                  chargeTotal =
              (bytes[4] +
              (bytes[5] << 8) +
              (bytes[6] << 16) +
              (bytes[7] << 24)) / 1000.0;
                  /*if (mainActivity.currentTab.trip.chargeStart == 0) {
                    mainActivity.currentTab.trip.chargeStart = chargeTotal;
                    mainActivity.SaveTabs();
                  }
                  charge = chargeTotal - mainActivity.currentTab.trip.chargeStart;*/
                  return chargeTotal;
                });

      p.AddValue("Discharge total", "kWH", "b",
          (bytes) => {
            dischargeTotal =
                  (bytes[0] +
                    (bytes[1] << 8) +
                    (bytes[2] << 16) +
                    (bytes[3] << 24)) / 1000.0;
            /*if (mainActivity.currentTab.trip.dischargeStart == 0) {
              mainActivity.currentTab.trip.dischargeStart = dischargeTotal;
              mainActivity.SaveTabs();
            }
            discharge = dischargeTotal - mainActivity.currentTab.trip.dischargeStart;*/
            return dischargeTotal;
          });

      p.AddValue("Discharge cycles", "x", "b",
        (bytes) => nominalFullPackEnergy > 0 ? dischargeTotal / nominalFullPackEnergy : (double?)null,
          new int[] { 0x382 });
      p.AddValue("Charge cycles", "x", "b",
        (bytes) => nominalFullPackEnergy > 0 ? chargeTotal / nominalFullPackEnergy : (double?)null,
          new int[] { 0x382 });

      /*p.AddValue("RegenFromCharge", "kWh", "tr",
          (bytes) => charge - acCharge - dcCharge,
          new int[] { 0x3F2 });
      p.AddValue("EnergyFromDischarge", "kWh", "tr",
          (bytes) => discharge - (charge - acCharge - dcCharge),
          new int[] { 0x3F2 });*/
      /*p.AddValue("Discharge", "kWh", "r",
          (bytes) => discharge);
      p.AddValue("Charge", "kWh", "r",
          (bytes) => charge);
      p.AddValue("Stationary", "kWh", "tr",
          (bytes) => ((discharge - (charge - acCharge - dcCharge)) - drive),
          new int[] { 0x3F2 });*/


      /*packets.Add(0x401, p = new Packet(0x401, this));
      p.AddValue("Last cell block updated", "xb", "", (bytes) => {
        int cell = 0;
        double voltage = 0.0;
        for (int i = 0; i < 3; i++) {
          voltage = ((bytes[i * 2 + 3] << 8) + bytes[i * 2 + 2]) / 10000.0;
          if (voltage > 0)
            UpdateItem("Cell " + (cell = ((bytes[0]) * 3 + i + 1)).ToString().PadLeft(2) + " voltage"
              , "zVC"
              , "z"
              , bytes[0]
              , voltage
              , 0x401);
        }
        if (cell > numCells)
          numCells = cell;
        var values = items.Where(x => x.Value.unit == "zVC");*/
      /*double min = values.Min(x => x.Value.GetValue(false));
      double max = values.Max(x => x.Value.GetValue(false));
      double avg = values.Average(x => x.Value.GetValue(false));
      UpdateItem("Cell min", "Vc", "bz", 0, min, 0x401);
      UpdateItem("Cell avg", "Vc", "bpz", 1, avg, 0x401);
      UpdateItem("Cell max", "Vc", "bz", 2, max, 0x401);
      UpdateItem("Cell diff", "Vcd", "bz", 3, max - min, 0x401);*/

      /*return bytes[0];
    });*/

      /*packets.Add(0x712, p = new Packet(0x712, this));
      p.AddValue("Last cell block updated", "xb", "", (bytes) => {
        int cell = 0;
        double voltage = 0.0;
        for (int i = 0; i < 3; i++) {
          voltage = (((bytes[i * 2 + 3] << 8) + bytes[i * 2 + 2]) /100.0);
          if (voltage > 0)
            UpdateItem("Cell " + (cell = ((bytes[0]) * 3 + i + 1)).ToString().PadLeft(2) + " temp"
              , "zVC"
              , "z"
              , bytes[0]
              , voltage
              , 0x712);
        }

        return bytes[0];
      });
      */

      // these are placeholders for the filters to be generated correctly.
      /*p.AddValue("Cell temp min", "C", "b", null);
      p.AddValue("Cell temp avg", "C", "bcp", null);
      p.AddValue("Cell temp max", "C", "b", null);
      p.AddValue("Cell temp diff", "Cd", "bc", null);
      p.AddValue("Cell min", "Vc", "b", null);
      p.AddValue("Cell avg", "Vc", "bpzr", null);
      p.AddValue("Cell max", "Vc", "b", null);
      p.AddValue("Cell diff", "Vcd", "bz", null);
      for (int i = 1; i <= 96; i++)
        p.AddValue("Cell " + i.ToString().PadLeft(2) + " voltage"
          , "zVC"
          , "z", null);
      for (int i = 1; i <= 32; i++)
        p.AddValue("Cell " + i.ToString().PadLeft(2) + " temp"
          , "zCC"
          , "c"
          , null);

  */

      packets.Add(0x2D2, p = new Packet(0x2D2, this));
      p.AddValue("Max pack voltage", "V", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 16, 16, false, 0.01, 0));
      p.AddValue("Min pack voltage", "V", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 0, 16, false, 0.01, 0));
      p.AddValue("Max discharge current", "A", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 48, 14, false, 0.128, 0));
      p.AddValue("Max charge current", "A", "b", (bytes) =>
        maxChargeCurrent = ExtractSignalFromBytes(bytes, 32, 16, false, 0.1, 0));
      p.AddValue("Max charge power", "kW", "b", (bytes) =>
        maxChargeCurrent * volt / 1000);


      packets.Add(0x20C, p = new Packet(0x20C, this));
      p.AddValue("Blower speed target", "RPM", "h", (bytes) =>
        ExtractSignalFromBytes(bytes, 32, 10, false, 1, 0));
      p.AddValue("Evap enabled", "0/1", "h", (bytes) =>
        ExtractSignalFromBytes(bytes, 11, 1, false, 1, 0));
      p.AddValue("Evap temp", "C", "h", (bytes) =>
        ExtractSignalFromBytes(bytes, 13, 11, false, 0.1, -40));
      p.AddValue("Evap target", "C", "h", (bytes) =>
        ExtractSignalFromBytes(bytes, 24, 8, false, 0.2, 0));

      packets.Add(0x2B3, p = new Packet(0x2B3, this));
      p.AddValue("Duct left", "C", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 0)
          return ExtractSignalFromBytes(bytes, 4, 9, false, .3, -40);
        else return null;
      });
      p.AddValue("Duct right", "C", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 0)
          return ExtractSignalFromBytes(bytes, 13, 9, false, .3, -40);
        else return null;
      });
      p.AddValue("Heater left", "W", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 2)
          return ExtractSignalFromBytes(bytes, 28, 10, false, 5, 0);
        else return null;
      });
      p.AddValue("Heater right", "W", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 2)
          return ExtractSignalFromBytes(bytes, 38, 10, false, 5, 0);
        else return null;
      });
      p.AddValue("Cabin humidity", "%", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 2)
          return ExtractSignalFromBytes(bytes, 20, 8, false, 1, 0);
        else return null;
      });
      p.AddValue("Cabin temp probe", "C", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 0)
          return ExtractSignalFromBytes(bytes, 24, 8, false, .5, -40);
        else return null;
      });
      p.AddValue("Cabin temp mid", "C", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 0)
          return ExtractSignalFromBytes(bytes, 32, 8, false, .5, -40);
        else return null;
      });
      p.AddValue("Cabin temp deep", "C", "h", (bytes) => {
        if ((bytes[0] & 0xF) == 0)
          return ExtractSignalFromBytes(bytes, 40, 8, false, .5, -40);
        else return null;
      });


      packets.Add(0x261, p = new Packet(0x261, this));
      p.AddValue("12v battery volt", "V", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 0, 12, false, 0.00544368, 0));
      p.AddValue("12v battery current", "A", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 48, 16, true, 0.005, 0));
      p.AddValue("12v battery Amp hours", "Ah", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 32, 14, true, 0.01, 0));
      p.AddValue("12v battery temp", "C", "b", (bytes) =>
        ExtractSignalFromBytes(bytes, 16, 16, true, 0.01, 0));

    }


  }

}

