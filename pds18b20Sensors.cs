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

    /// <summary>
    /// This class contains the handle for this module. In this case, it is a list of active DS18B20 sensors.
    /// </summary>
    class ModuleHandle
    {
        public static async Task<ModuleHandle> CreateHandleFromConfiguration(ModuleConfig config)
        {
            pids18b20.Sensors.ModuleHandle moduleHandle = null;
            foreach (var config_pair in config.SensorConfigs)
            {
                SensorConfig sensorConfig = config_pair.Value;
                switch (sensorConfig.GetConnectionType())
                {
                    case ModbusConstants.ConnectionType.ModbusTCP:
                        {
                            if (moduleHandle == null)
                            {
                                moduleHandle = new pids18b20.Sensors.ModuleHandle();
                            }

                            SensorInformation slave = new ModbusTCPSlaveSession(sensorConfig);
                            await slave.InitSession();
                            moduleHandle.ModbusSessionList.Add(slave);
                            break;
                        }
                    case ModbusConstants.ConnectionType.ModbusRTU:
                        {
                            if (moduleHandle == null)
                            {
                                moduleHandle = new pids18b20.Sensors.ModuleHandle();
                            }

                            SensorInformation slave = new ModbusRTUSlaveSession(sensorConfig);
                            await slave.InitSession();
                            moduleHandle.ModbusSessionList.Add(slave);
                            break;
                        }
                    case ModbusConstants.ConnectionType.ModbusASCII:
                        {
                            break;
                        }
                    case ModbusConstants.ConnectionType.Unknown:
                        {
                            break;
                        }
                }
            }
            return moduleHandle;
        }
        public List<SensorInformation> ModbusSessionList = new List<SensorInformation>();
        public SensorInformation GetSlaveSession(string hwid)
        {
            return ModbusSessionList.Find(x => x.config.HwId.ToUpper() == hwid.ToUpper());
        }
        public void Release()
        {
            foreach (var session in ModbusSessionList)
            {
                session.ReleaseSession();
            }
            ModbusSessionList.Clear();
        }
        public List<object> CollectAndResetOutMessageFromSessions()
        {
            List<object> obj_list = new List<object>();

            foreach (SensorInformation session in ModbusSessionList)
            {
                var obj = session.GetOutMessage();
                if (obj != null)
                {
                    obj_list.Add(obj);
                    session.ClearOutMessage();
                }
            }
            return obj_list;
        }
    }

    /// <summary>
    /// Base class of Modbus session.
    /// </summary>


    abstract class SensorInformation
    {
        public SensorConfig config;
        protected object OutMessage = null;
        protected const int m_bufSize = 512;
        protected SemaphoreSlim m_semaphore_collection = new SemaphoreSlim(1, 1);
        protected SemaphoreSlim m_semaphore_connection = new SemaphoreSlim(1, 1);
        protected bool m_run = false;
        protected List<Task> m_taskList = new List<Task>();
        protected virtual int m_reqSize { get; }
        protected virtual int m_dataBodyOffset { get; }
        protected virtual int m_silent { get; }

        #region Constructors
        public SensorInformation(SensorConfig conf)
        {
            config = conf;
        }
        #endregion

        #region Public Methods
        public abstract void ReleaseSession();
        public async Task InitSession()
        {
            await ConnectSlave();

            foreach (var op_pair in config.Operations)
            {
                ReadOperation x = op_pair.Value;
                
                x.RequestLen = m_reqSize;
                x.Request = new byte[m_bufSize];

                EncodeRead(x);
            }
        }
        public async Task WriteCB(string uid, string address, string value)
        {
            byte[] writeRequest = new byte[m_bufSize];
            byte[] writeResponse = null;
            int reqLen = m_reqSize;

            EncodeWrite(writeRequest, uid, address, value);
            writeResponse = await SendRequest(writeRequest, reqLen);
        }
        public void ProcessOperations()
        {
            m_run = true;
            foreach (var op_pair in config.Operations)
            {
                ReadOperation x = op_pair.Value;
                Task t = Task.Run(async () => await SingleOperation(x));
                m_taskList.Add(t);
            }
        }
        public object GetOutMessage()
        {
            return OutMessage;
        }
        public void ClearOutMessage()
        {
            m_semaphore_collection.Wait();

            OutMessage = null;

            m_semaphore_collection.Release();
        }
        #endregion

        #region Protected Methods
        protected abstract void EncodeWrite(byte[] request, string uid, string address, string value);
        protected abstract Task<byte[]> SendRequest(byte[] request, int reqLen);
        protected abstract Task ConnectSlave();
        protected abstract void EncodeRead(ReadOperation operation);
        protected async Task SingleOperation(ReadOperation x)
        {
            while (m_run)
            {
                x.Response = null;
                x.Response = await SendRequest(x.Request, x.RequestLen);

                if (x.Response != null)
                {
                    if (x.Request[m_dataBodyOffset] == x.Response[m_dataBodyOffset])
                    {
                        ProcessResponse(config, x);
                    }
                    else if (x.Request[m_dataBodyOffset] + ModbusConstants.ModbusExceptionCode == x.Response[m_dataBodyOffset])
                    {
                        Console.WriteLine($"Modbus exception code: {x.Response[m_dataBodyOffset + 1]}");
                    }
                }
                await Task.Delay(x.PollingInterval - m_silent);
            }
        }
        protected void ProcessResponse(SensorConfig config, ReadOperation x)
        {
            int count = 0;
            int step_size = 0;
            int start_digit = 0;
            List<ModbusOutValue> value_list = new List<ModbusOutValue>();
            switch (x.Response[m_dataBodyOffset])//function code
            {
                case (byte)ModbusConstants.FunctionCodeType.ReadCoils:
                case (byte)ModbusConstants.FunctionCodeType.ReadInputs:
                    {
                        count = x.Response[m_dataBodyOffset + 1] * 8;
                        count = (count > x.Count) ? x.Count : count;
                        step_size = 1;
                        start_digit = x.Response[m_dataBodyOffset] - 1;
                        break;
                    }
                case (byte)ModbusConstants.FunctionCodeType.ReadHoldingRegisters:
                case (byte)ModbusConstants.FunctionCodeType.ReadInputRegisters:
                    {
                        count = x.Response[m_dataBodyOffset + 1];
                        step_size = 2;
                        start_digit = (x.Response[m_dataBodyOffset] == 3) ? 4 : 3;
                        break;
                    }
            }
            for (int i = 0; i < count; i += step_size)
            {
                string res = "";
                string cell = "";
                string val = "";
                if (step_size == 1)
                {
                    cell = string.Format(x.OutFormat, (char)x.EntityType, x.Address + i + 1);
                    val = string.Format("{0}", (x.Response[m_dataBodyOffset + 2 + (i / 8)] >> (i % 8)) & 0b1);
                }
                else if (step_size == 2)
                {
                    cell = string.Format(x.OutFormat, (char)x.EntityType, x.Address + (i / 2) + 1);
                    val = string.Format("{0,00000}", ((x.Response[m_dataBodyOffset + 2 + i]) * 0x100 + x.Response[m_dataBodyOffset + 3 + i]));
                }
                res = cell + ": " + val + "\n";
                Console.WriteLine(res);

                ModbusOutValue value = new ModbusOutValue()
                { DisplayName = x.DisplayName, Address = cell, Value = val };
                value_list.Add(value);
            }

            if (value_list.Count > 0)
                PrepareOutMessage(config.HwId, x.CorrelationId, value_list);
        }
        protected void PrepareOutMessage(string HwId, string CorrelationId, List<ModbusOutValue> ValueList)
        {
            m_semaphore_collection.Wait();
            ModbusOutContent content = null;
            if (OutMessage == null)
            {
                content = new ModbusOutContent
                {
                    HwId = HwId,
                    Data = new List<ModbusOutData>()
                };
                OutMessage = content;
            }
            else
            {
                content = (ModbusOutContent)OutMessage;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ModbusOutData data = null;
            foreach(var d in content.Data)
            {
                if (d.CorrelationId == CorrelationId && d.SourceTimestamp == timestamp)
                {
                    data = d;
                    break;
                }
            }
            if(data == null)
            {
                data = new ModbusOutData
                {
                    CorrelationId = CorrelationId,
                    SourceTimestamp = timestamp,
                    Values = new List<ModbusOutValue>()
                };
                content.Data.Add(data);
            }

            data.Values.AddRange(ValueList);

            m_semaphore_collection.Release();

        }
        protected void ReleaseOperations()
        {
            m_run = false;
            Task.WaitAll(m_taskList.ToArray());
            m_taskList.Clear();
        }
        #endregion
    }

 
    /// <summary>
    /// This class contains the configuration for a Modbus session.
    /// </summary>
//devicedir=/sys/bus/w1/devices/
//# Sensor symbolic names
//sensor=High (AC),No Dot,28-0416514b65ff
//sensor=Desktop,Blue Dot,28-041651547eff
//sensor=Floor 1,No Dot,28-0416510d05ff
//sensor=Floor 2,No Dot,28-0316459b64ff

    class SensorConfig
    {
        public string SlaveConnection { get; set; }
        public int RetryCount { get; set; }
        public int RetryInterval { get; set; }
        public int TcpPort { get; set; }
        public string HwId { get; set; }
        public uint BaudRate { get; set; }
        public StopBits StopBits { get; set; }
        public byte DataBits { get; set; }
        public Parity Parity { get; set; }
        //public byte FlowControl { get; set; }
        public Dictionary<string, ReadOperation> Operations = null;
        public ModbusConstants.ConnectionType GetConnectionType()
        {
            if (IPAddress.TryParse(SlaveConnection, out IPAddress address))
                return ModbusConstants.ConnectionType.ModbusTCP;
            else if (SlaveConnection.Substring(0, 3) == "COM" || SlaveConnection.Substring(0, 8) == "/dev/tty")
                return ModbusConstants.ConnectionType.ModbusRTU;
            //TODO: ModbusRTU ModbusASCII
            return ModbusConstants.ConnectionType.Unknown;
        }

    }

    /// <summary>
    /// This class contains the configuration for a single Modbus read request.
    /// </summary>
    class ReadOperation
    {
        public byte[] Request;
        public byte[] Response;
        public int RequestLen;
        public byte EntityType { get; set; }
        public string OutFormat { get; set; }
        public int PollingInterval { get; set; }
        public byte UnitId { get; set; }
        public byte FunctionCode { get; set; }
        public string StartAddress { get; set; }
        public UInt16 Address { get; set; }
        public UInt16 Count { get; set; }
        public string DisplayName { get; set; }
        public string CorrelationId { get; set; }
    }

    static class ModbusConstants
    {
        public enum EntityType
        {
            CoilStatus = '0',
            InputStatus = '1',
            InputRegister = '3',
            HoldingRegister = '4'
        }
        public enum ConnectionType
        {
            Unknown = 0,
            ModbusTCP = 1,
            ModbusRTU = 2,
            ModbusASCII = 3
        };
        public enum FunctionCodeType
        {
            ReadCoils = 1,
            ReadInputs = 2,
            ReadHoldingRegisters = 3,
            ReadInputRegisters = 4,
            WriteCoil = 5,
            WriteHoldingRegister = 6
        };
        public static int DefaultTcpPort = 502;
        public static int DefaultRetryCount = 10;
        public static int DefaultRetryInterval = 50;
        public static string DefaultCorrelationId = "DefaultCorrelationId";
        public static int ModbusExceptionCode = 0x80;
    }
    
    class ModbusOutContent
    {
        public string HwId { get; set; }
        public List<ModbusOutData> Data { get; set; }
    }

    class ModbusOutData
    {
        public string CorrelationId { get; set; }
        public string SourceTimestamp { get; set; }
        public List<ModbusOutValue> Values { get; set; }
    }
    class ModbusOutValue
    {
        public string DisplayName { get; set; }
        //public string OpName { get; set; }
        public string Address { get; set; }
        public string Value { get; set; }
    }

    class ModbusInMessage
    {
        public string HwId { get; set; }
        public string UId { get; set; }
        public string Address { get; set; }
        public string Value { get; set; }
    }

    class ModuleConfig
    {
        public string SensorDevicePath;
        public Dictionary<string, SensorConfig> SensorConfigs;
        public ModuleConfig(Dictionary<string, SensorConfig> sensors)
        {
            SensorConfigs = sensors;
        }
        public bool IsValidate()
        {
            bool ret = true;

            foreach (var config_pair in SensorConfigs)
            {
                SensorConfig sensorConfig = config_pair.Value;
                if (sensorConfig.TcpPort <= 0)
                {
                    Console.WriteLine($"Invalid TcpPort: {sensorConfig.TcpPort}, set to DefaultTcpPort: {ModbusConstants.DefaultTcpPort}");
                    sensorConfig.TcpPort = ModbusConstants.DefaultTcpPort;
                }
                if (sensorConfig.RetryCount <= 0)
                {
                    Console.WriteLine($"Invalid RetryCount: {sensorConfig.RetryCount}, set to DefaultRetryCount: {ModbusConstants.DefaultRetryCount}");
                    sensorConfig.RetryCount = ModbusConstants.DefaultRetryCount;
                }
                if (sensorConfig.RetryInterval <= 0)
                {
                    Console.WriteLine($"Invalid RetryInterval: {sensorConfig.RetryInterval}, set to DefaultRetryInterval: {ModbusConstants.DefaultRetryInterval}");
                    sensorConfig.RetryInterval = ModbusConstants.DefaultRetryInterval;
                }
                foreach (var operation_pair in sensorConfig.Operations)
                {
                    ReadOperation operation = operation_pair.Value;
                    ParseEntity(operation.StartAddress, true, out ushort address_int16, out byte function_code, out byte entity_type);

                    if (operation.Count <= 0)
                    {
                        Console.WriteLine($"Invalid Count: {operation.Count}");
                        ret = false;
                    }
                    if (operation.Count > 127 && ((char)entity_type == (char)ModbusConstants.EntityType.HoldingRegister || (char)entity_type == (char)ModbusConstants.EntityType.InputRegister))
                    {
                        Console.WriteLine($"Invalid Count: {operation.Count}, must be 1~127");
                        ret = false;
                    }
                    if(operation.CorrelationId == "" || operation.CorrelationId == null)
                    {
                        Console.WriteLine($"Empty CorrelationId: {operation.CorrelationId}, set to DefaultCorrelationId: {ModbusConstants.DefaultCorrelationId}");
                        operation.CorrelationId = ModbusConstants.DefaultCorrelationId;
                    }
                    if (ret)
                    {
                        operation.EntityType = entity_type;
                        operation.Address = address_int16;
                        operation.FunctionCode = function_code;
                        //output format
                        if (operation.StartAddress.Length == 5)
                            operation.OutFormat = "{0}{1:0000}";
                        else if (operation.StartAddress.Length == 6)
                            operation.OutFormat = "{0}{1:00000}";
                    }
                }
            }

            return ret;
        }
        public static bool ParseEntity(string startAddress, bool isRead, out ushort outAddress, out byte functionCode, out byte entityType)
        {
            outAddress = 0;
            functionCode = 0;

            byte[] entity_type = Encoding.ASCII.GetBytes(startAddress, 0, 1);
            entityType = entity_type[0];
            string address_str = startAddress.Substring(1);
            int address_int = Convert.ToInt32(address_str);

            //function code
            switch ((char)entityType)
            {
                case (char)ModbusConstants.EntityType.CoilStatus:
                    {
                        functionCode = (byte)(isRead ? ModbusConstants.FunctionCodeType.ReadCoils : ModbusConstants.FunctionCodeType.WriteCoil);
                        break;
                    }
                case (char)ModbusConstants.EntityType.InputStatus:
                    {
                        if (isRead)
                            functionCode = (byte)ModbusConstants.FunctionCodeType.ReadInputs;
                        else
                            return false;
                        break;
                    }
                case (char)ModbusConstants.EntityType.InputRegister:
                    {
                        if (isRead)
                            functionCode = (byte)ModbusConstants.FunctionCodeType.ReadInputRegisters;
                        else
                            return false;
                        break;
                    }
                case (char)ModbusConstants.EntityType.HoldingRegister:
                    {
                        functionCode = (byte)(isRead ? ModbusConstants.FunctionCodeType.ReadHoldingRegisters : ModbusConstants.FunctionCodeType.WriteHoldingRegister);
                        break;
                    }
                default:
                    {
                        return false;
                    }
            }
            //address
            outAddress = (UInt16)(address_int - 1);
            return true;
        }
    }

    class ModbusPushInterval
    {
        public ModbusPushInterval(int interval)
        {
            PublishInterval = interval;
        }
        public int PublishInterval { get; set; }
    }
    class ModbusOutMessage
    {
        public string PublishTimestamp { get; set; }
        public List<object> Content { get; set; }
    }
}
