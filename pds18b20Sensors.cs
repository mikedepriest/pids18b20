namespace pids18b20.Sensors
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using System.IO.Ports;
    using System.Runtime.InteropServices;    
    
    static class ModuleConfigDefaults
    {
         public const string DefaultSensorDevicePath = "/sys/bus/w1/devices/";
         public const string DefaultSensorDevicePathSuffix = "/w1_slave";
         public const int DefaultPublishIntervalSeconds = 60;
         public const string DefaultSensorName = "unnamed";
         public const string DefaultSensorDescription = "no description";
         public const string DefaultSensorUOM = "C";
         public const Boolean DefaultVerboseLogging = false;

    }
    
    /// <summary>
    /// This class contains the configuration for a DS18B20.
    /// </summary>

    class SensorConfig
    {
        public string SensorName { get; set; }
        public string SensorDescription { get; set; }
        public string SensorId { get; set; }
        public string UOM { get; set; }

        public override string ToString() => $"SensorName: [{SensorName}] SensorDescription: [{SensorDescription}] SensorId: [{SensorId}] UOM: [{UOM}]";
    }

    class SensorReading
    {
        public string PublishTimestamp { get; set; }
        public string SensorName { get; set; }
        public float Temperature { get; set; }
        public string UOM { get; set; }

        public SensorReading()
        {
            PublishTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SensorName = ModuleConfigDefaults.DefaultSensorName;
            Temperature = -9999;
            UOM = ModuleConfigDefaults.DefaultSensorUOM;
        }

        public override string ToString() => $"PublishTimestamp: [{PublishTimestamp}] SensorName: [{SensorName}] Temperature: [{Temperature}] UOM: [{UOM}]";

        public SensorReading(ModuleConfig mc, string sensorId)
        {
            SensorConfig sc = mc.SensorConfigs[sensorId];

            PublishTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SensorName = string.IsNullOrEmpty(sc.SensorName) ? ModuleConfigDefaults.DefaultSensorName : sc.SensorName;
            UOM = string.IsNullOrEmpty(sc.UOM) ? ModuleConfigDefaults.DefaultSensorUOM : sc.UOM;
            Temperature = -9999;

            string sensorfile = mc.SensorDevicePath + sc.SensorId + mc.SensorDevicePathSuffix;
            bool deviceReadOk = false;

            if (mc.VerboseLogging) Console.WriteLine("Opening file: "+sensorfile);
            // Open the sensor file 
            string[] lines = {"NO","READING=-9999"};
            try 
            {
                lines = System.IO.File.ReadAllLines(sensorfile);
                deviceReadOk = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to read device file "+sensorfile+": "+ex.Message);
            }

            if (deviceReadOk)
            {
                // DS18B20 device files have the following structure:
                // Line 1: Status
                // Line 2: Reading
                // Status will be YES if the reading is valid, if it's not YES then we should report an error
                // Reading takes the form of string=number, where number is the Celcius temperature in thousandths of a degree

                if (lines[0].Contains("YES"))
                {
                    string[] readingString = lines[1].Split('=');
                    float readingC1000 = Convert.ToSingle(readingString[1]);
                    Temperature = readingC1000 / (float) 1000.0;
                    if (sc.UOM.Equals("F")) Temperature = (float) 32 + ( (float) 1.8 * Temperature );
                }
            }
        }
    }

    
    class ModuleConfig
    {
        public string SensorDevicePath = ModuleConfigDefaults.DefaultSensorDevicePath;
        public string SensorDevicePathSuffix = ModuleConfigDefaults.DefaultSensorDevicePathSuffix;
        public int PublishIntervalSeconds = ModuleConfigDefaults.DefaultPublishIntervalSeconds;
        public Boolean VerboseLogging = ModuleConfigDefaults.DefaultVerboseLogging; 
        public Dictionary<string, SensorConfig> SensorConfigs;
        public ModuleConfig(string sensorDevicePath, string sensorDevicePathSuffix, int publishIntervalSeconds, Boolean verboseLogging, Dictionary<string, SensorConfig> sensors)
        {
            SensorDevicePath = sensorDevicePath;
            SensorDevicePathSuffix = sensorDevicePathSuffix;
            PublishIntervalSeconds = publishIntervalSeconds;
            VerboseLogging = verboseLogging;
            SensorConfigs = sensors;
        }

        public override string ToString() 
        {
            string out1 = $"SensorDevicePath: [{SensorDevicePath}] SensorDevicePathSuffix: [{SensorDevicePathSuffix}] PublishIntervalSeconds: [{PublishIntervalSeconds}] VerboseLogging: [{VerboseLogging}]";
            foreach(string sck in SensorConfigs.Keys)
            {
                out1=out1+$"\nSensorId: [{SensorConfigs[sck]}";
            }
            return out1;
        }
        public bool IsValid()
        {
            bool ret = true;
            
            // SensorDevicePath must be present, publish interval must be positive
            if(string.IsNullOrEmpty(SensorDevicePath)) {
                Console.WriteLine("Missing or empty value for SensorDevicePath");
                ret=false;
            } else if(PublishIntervalSeconds <= 0) {
                Console.WriteLine("Invalid value for PublishIntervalSeconds: ",PublishIntervalSeconds);
                ret=false;
            } else {
                // Validate individual sensor configs
                foreach (var config_pair in SensorConfigs)
                {
                    SensorConfig sensorConfig = config_pair.Value;
                    // The only absolutely required field is the 1Wire ID for the sensor
                    if(string.IsNullOrEmpty(sensorConfig.SensorId)) {
                        Console.WriteLine("Key: ",config_pair.Key,": Missing or empty value for SensorId");
                        ret=false;
                        break;
                    }
                    
                }
            }
            return ret;
        }
        
        public SensorReading ReadSensor(string sensorId) {
            SensorReading ret = new SensorReading();
            if (this.VerboseLogging) Console.WriteLine($"Reading sensor: [{sensorId}]");
                
            try
            {
                ret = new SensorReading(this, sensorId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Reading exception:",ex.ToString());
            }
            return ret;
        }   
    }
}
