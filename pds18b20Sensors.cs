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

        public SensorReading(string devicePath, string devicePathSuffix, SensorConfig sc)
        {
            PublishTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SensorName = string.IsNullOrEmpty(sc.SensorName) ? ModuleConfigDefaults.DefaultSensorName : sc.SensorName;
            UOM = string.IsNullOrEmpty(sc.UOM) ? ModuleConfigDefaults.DefaultSensorUOM : sc.UOM;
            Temperature = -9999;

            string sensorfile = devicePath + sc.SensorId + devicePathSuffix;
            bool deviceReadOk = false;

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
                }
            }
        }
    }

    
    class ModuleConfig
    {
        public string SensorDevicePath = ModuleConfigDefaults.DefaultSensorDevicePath;
        public string SensorDevicePathSuffix = ModuleConfigDefaults.DefaultSensorDevicePathSuffix;
        public int PublishIntervalSeconds = ModuleConfigDefaults.DefaultPublishIntervalSeconds; 
        public Dictionary<string, SensorConfig> SensorConfigs;
        public ModuleConfig(string sensorDevicePath, int publishIntervalSeconds, Dictionary<string, SensorConfig> sensors)
        {
            SensorDevicePath = sensorDevicePath;
            PublishIntervalSeconds = publishIntervalSeconds;
            SensorConfigs = sensors;
        }
        public bool IsValid()
        {
            bool ret = true;
            
            // SensorDevicePath must be present, publish interval must be positive
            if(string.IsNullOrEmpty(SensorDevicePath)) {
                ret=false;
            } else if(PublishIntervalSeconds <= 0) {
                ret=false;
            } else {
                // Validate individual sensor configs
                foreach (var config_pair in SensorConfigs)
                {
                    SensorConfig sensorConfig = config_pair.Value;
                    // The only absolutely required field is the 1Wire ID for the sensor
                    if(string.IsNullOrEmpty(sensorConfig.SensorId)) {
                        ret=false;
                        break;
                    }
                    
                }
            }
            return ret;
        }
        
        public SensorReading ReadSensor(SensorConfig sc) {
            SensorReading ret = new SensorReading(this.SensorDevicePath, this.SensorDevicePathSuffix, sc);
            return ret;
        }   
    }
}
