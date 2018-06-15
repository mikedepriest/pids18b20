namespace pids18b20
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using System.Collections.Generic;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using pids18b20.Sensors;


    class Program
    {
        static int counter;
        const string DesiredPropertyKey = "DS18B20Configs";
        const int DefaultPushInterval = 10000;
        static List<Task> m_task_list = new List<Task>();
        static bool m_run = true;
        
        static void Main(string[] args)
        {
            // Install CA certificate
            InstallCert();

            // Initialize Edge Module
            InitEdgeModule().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
           // Suppress cert validation on Windows for now
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            Console.WriteLine("Added Cert: " + certPath);
            store.Close();
        }


        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task InitEdgeModule()
        {
            try
            {
                // START Boilerplate

                // Open a connection to the Edge runtime using MQTT transport and
                // the connection string provided as an environment variable
                string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");

                MqttTransportSettings mqttSettings = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
                // Suppress cert validation on Windows for now
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    mqttSettings.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                }
                ITransportSettings[] settings = { mqttSettings };

                DeviceClient ioTHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);
                await ioTHubModuleClient.OpenAsync();

                //END Boilerplate

                Console.WriteLine("IoT Hub module client for pids18b20 initialized.");

                // Read config from Twin and Start
                Twin moduleTwin = await ioTHubModuleClient.GetTwinAsync();
                await UpdateStartFromTwin(moduleTwin.Properties.Desired, ioTHubModuleClient);

                // Attach callback for Twin desired properties updates
                await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, ioTHubModuleClient);

            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when initializing module: {0}", exception);
                }
            }

        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            Console.WriteLine("pids18b20 - Received command");
            int counterValue = Interlocked.Increment(ref counter);
            DeviceClient deviceClient = (DeviceClient)userContext;
            
            var userContextValues = userContext as Tuple<DeviceClient, Sensors.ModuleConfig>;
            if (userContextValues == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " +
                    "expected values");
            }
            DeviceClient ioTHubModuleClient = userContextValues.Item1;
            Sensors.ModuleConfig moduleConfig = userContextValues.Item2;

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                await deviceClient.SendEventAsync("output1", pipeMessage);
                Console.WriteLine("Received message sent");
            }
            return MessageResponse.Completed;
        }

                /// <summary>�
        /// Callback to handle Twin desired properties updates�
        /// </summary>�
        static async Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            DeviceClient ioTHubModuleClient = userContext as DeviceClient;

            try
            {
                // stop all activities while updating configuration
                await ioTHubModuleClient.SetInputMessageHandlerAsync(
                "input1",
                DummyCallBack,
                null);

                m_run = false;
                await Task.WhenAll(m_task_list);
                m_task_list.Clear();
                m_run = true;

                await UpdateStartFromTwin(desiredProperties, ioTHubModuleClient);
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when receiving desired property: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error when receiving desired property: {0}", ex.Message);
            }
        }

        /// <summary>
        /// A dummy callback does nothing
        /// </summary>
        /// <param name="message"></param>
        /// <param name="userContext"></param>
        /// <returns></returns>
        static async Task<MessageResponse> DummyCallBack(Message message, object userContext)
        {
            await Task.Delay(TimeSpan.FromSeconds(0));
            return MessageResponse.Abandoned;
        }

        /// <summary>
        /// Update Start from module Twin. 
        /// </summary>
        static async Task UpdateStartFromTwin(TwinCollection desiredProperties, DeviceClient ioTHubModuleClient)
        {
            ModuleConfig config;
            string jsonStr = null;
            string serializedStr;

            serializedStr = JsonConvert.SerializeObject(desiredProperties);
            Console.WriteLine("Desired property change:");
            Console.WriteLine(serializedStr);

            if (desiredProperties.Contains(DesiredPropertyKey))
            {
                // get config from Twin
                jsonStr = serializedStr;
            }
            else
            {
                Console.WriteLine("No configuration found in desired properties, look in local pids18b20.json");
                if (File.Exists(@"pids18b20.json"))
                {
                    try
                    {
                        // get config from local file
                        jsonStr = File.ReadAllText(@"pids18b20.json");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Load configuration error: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("No local configuration file found");
                }
            }

            if (!string.IsNullOrEmpty(jsonStr))
            {
                Console.WriteLine("Attempt to load configuration: " + jsonStr);
                config = JsonConvert.DeserializeObject<ModuleConfig>(jsonStr);
                
                if (config.IsValid())
                {
                    var userContext = new Tuple<DeviceClient, Sensors.ModuleConfig>(ioTHubModuleClient, config);
                    // Register callback to be called when a message is received by the module
                    await ioTHubModuleClient.SetInputMessageHandlerAsync("input1",PipeMessage,userContext);
                    m_task_list.Add(Start(userContext));                    
                }
            }
        }

        /// <summary>
        /// Iterate through each sensor to poll data 
        /// </summary>
        static async Task Start(object userContext)
        {
            var userContextValues = userContext as Tuple<DeviceClient, Sensors.ModuleConfig>;
            if (userContextValues == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " +
                    "expected values");
            }

            DeviceClient ioTHubModuleClient = userContextValues.Item1;
            Sensors.ModuleConfig moduleConfig = userContextValues.Item2;

            while (m_run)
            {
                foreach (string s in moduleConfig.SensorConfigs.Keys)
                {
                    SensorConfig sc = moduleConfig.SensorConfigs[s];
                
                    SensorReading sensorReading = moduleConfig.ReadSensor(sc);
                    Message message = null;
                    message = new Message(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(sensorReading)));
                    message.Properties.Add("content-type", "application/edge-ds18b20-json");

                    if (message != null)
                    {
                        await ioTHubModuleClient.SendEventAsync("sensorOutput", message);
                    }
                    if (!m_run)
                    {
                        break;
                    }
                    await Task.Delay(1000*moduleConfig.PublishIntervalSeconds);
                }
            }
        }

        /// <summary>
        /// Receive C2D message(running without iot edge)
        /// </summary>
        /// <param name="userContext"></param>
        /// <returns></returns>
        static async Task Receive(object userContext)
        {
            var userContextValues = userContext as Tuple<DeviceClient, Sensors.ModuleConfig>;
            if (userContextValues == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " +
                    "expected values");
            }
            DeviceClient ioTHubModuleClient = userContextValues.Item1;
            var timeout = TimeSpan.FromSeconds(3);
            while (m_run)
            {
                try
                {
                    Message message = await ioTHubModuleClient.ReceiveAsync(timeout);
                    if (message != null)
                    {
                        await PipeMessage(message, userContext);
                        await ioTHubModuleClient.CompleteAsync(message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when receiving: {0}", ex.Message);
                }
            }
        }
        
    }
}
