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
         public const int DefaultPublishIntervalSeconds = 60;
         public const string DefaultSensorName = "unnamed";
         public const string DefaultSensorDescription = "no description";
         public const string DefaultSensorUOM = "C";

    }
    
    /// <summary>
    /// This class contains the configuration for a DS18B20.
    /// </summary>
//devicedir=/sys/bus/w1/devices/
//# Sensor symbolic names
//sensor=High (AC),No Dot,28-0416514b65ff
//sensor=Desktop,Blue Dot,28-041651547eff
//sensor=Floor 1,No Dot,28-0416510d05ff
//sensor=Floor 2,No Dot,28-0316459b64ff

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

        public SensorReading(SensorConfig sc)
        {
            PublishTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SensorName = string.IsNullOrEmpty(sc.SensorName) ? ModuleConfigDefaults.DefaultSensorName : sc.SensorName;
            UOM = string.IsNullOrEmpty(sc.UOM) ? ModuleConfigDefaults.DefaultSensorUOM : sc.UOM;
            Temperature = -9999;
        }

    }

    
    class ModuleConfig
    {
        public string SensorDevicePath = ModuleConfigDefaults.DefaultSensorDevicePath;
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
            SensorReading ret = new SensorReading(sc);
            return ret;
        }   
    }
}
