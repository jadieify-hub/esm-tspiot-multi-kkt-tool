using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal interface IAtolFptrRuntimeFactory
    {
        IAtolFptrRuntime Create(AtolDriverRuntime driverRuntime);
    }

    internal interface IAtolFptrRuntime
    {
        void Configure(string portName, bool autoReconnect);
        void Open();
        KktConnectionIdentity ReadIdentity();
        void Close();
        void Destroy();
    }

    internal sealed class AtolFptrConnectionProvider : IKktConnectionProvider
    {
        private readonly AtolDriverRuntimeLocator _runtimeLocator;
        private readonly AtolVcomEnumerator _ports;
        private readonly IAtolFptrRuntimeFactory _factory;

        internal AtolFptrConnectionProvider()
            : this(
                new AtolDriverRuntimeLocator(),
                new AtolVcomEnumerator(),
                new ReflectionAtolFptrRuntimeFactory())
        {
        }

        internal AtolFptrConnectionProvider(
            AtolDriverRuntimeLocator runtimeLocator,
            AtolVcomEnumerator ports,
            IAtolFptrRuntimeFactory factory)
        {
            if (runtimeLocator == null) throw new ArgumentNullException("runtimeLocator");
            if (ports == null) throw new ArgumentNullException("ports");
            if (factory == null) throw new ArgumentNullException("factory");
            _runtimeLocator = runtimeLocator;
            _ports = ports;
            _factory = factory;
        }

        public IList<KktConnectionPort> EnumeratePorts()
        {
            return _ports.EnumeratePorts();
        }

        public Task<IKktConnectionLease> OpenAsync(
            KktConnectionPort port,
            CancellationToken cancellationToken)
        {
            if (port == null) throw new ArgumentNullException("port");
            string portName = NormalizePort(port.PortName);
            return Task.Run<IKktConnectionLease>(delegate
            {
                cancellationToken.ThrowIfCancellationRequested();
                IAtolFptrRuntime runtime = null;
                bool opened = false;
                try
                {
                    AtolDriverRuntime driverRuntime = _runtimeLocator.Resolve();
                    runtime = _factory.Create(driverRuntime);
                    runtime.Configure(portName, false);
                    runtime.Open();
                    opened = true;
                    cancellationToken.ThrowIfCancellationRequested();
                    KktConnectionIdentity identity = runtime.ReadIdentity();
                    if (identity == null || string.IsNullOrWhiteSpace(identity.KktSerial))
                    {
                        throw new InvalidOperationException(
                            "Драйвер АТОЛ не вернул серийный номер ККТ.");
                    }
                    identity.PortName = portName;
                    return new AtolFptrConnectionLease(runtime, identity);
                }
                catch (OperationCanceledException)
                {
                    Release(runtime, opened);
                    throw;
                }
                catch (Exception ex)
                {
                    Release(runtime, opened);
                    throw new InvalidOperationException(
                        "Не удалось открыть ККТ через " + portName + ". " +
                        SafeMessage(ex), ex);
                }
            }, cancellationToken);
        }

        private static string NormalizePort(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Не указан VCOM ККТ.", "value");
            string trimmed = value.Trim();
            if (!trimmed.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Для авторегистрации нужен точный VCOM, а не USB:auto.", "value");
            int number;
            if (!int.TryParse(trimmed.Substring(3), out number) || number < 1)
                throw new ArgumentException("Неверный VCOM ККТ.", "value");
            return "COM" + number;
        }

        private static void Release(IAtolFptrRuntime runtime, bool opened)
        {
            if (runtime == null) return;
            if (opened)
            {
                try { runtime.Close(); }
                catch { }
            }
            try { runtime.Destroy(); }
            catch { }
        }

        private static string SafeMessage(Exception exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
                return "Драйвер не сообщил причину.";
            string message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (message.IndexOf(":\\", StringComparison.Ordinal) >= 0)
                return "Драйвер вернул ошибку; подробности с локальным путём скрыты.";
            return message.Length <= 240 ? message : message.Substring(0, 240);
        }

        private sealed class AtolFptrConnectionLease : IKktConnectionLease
        {
            private IAtolFptrRuntime _runtime;

            internal AtolFptrConnectionLease(
                IAtolFptrRuntime runtime,
                KktConnectionIdentity identity)
            {
                _runtime = runtime;
                Identity = identity;
            }

            public KktConnectionIdentity Identity { get; private set; }

            public void Dispose()
            {
                IAtolFptrRuntime runtime = Interlocked.Exchange(ref _runtime, null);
                if (runtime == null) return;
                try { runtime.Close(); }
                catch { }
                try { runtime.Destroy(); }
                catch { }
            }
        }
    }

    internal sealed class ReflectionAtolFptrRuntimeFactory : IAtolFptrRuntimeFactory
    {
        public IAtolFptrRuntime Create(AtolDriverRuntime driverRuntime)
        {
            if (driverRuntime == null) throw new ArgumentNullException("driverRuntime");
            Assembly wrapper = Assembly.LoadFrom(driverRuntime.WrapperPath);
            Type fptrType = wrapper.GetType("Atol.Drivers10.Fptr.Fptr", true, false);
            Type constantsType = wrapper.GetType("Atol.Drivers10.Fptr.Constants", true, false);
            object instance;
            try
            {
                instance = Activator.CreateInstance(
                    fptrType,
                    new object[] { driverRuntime.NativeLibraryPath });
            }
            catch (TargetInvocationException ex)
            {
                throw Unwrap(ex);
            }
            return new ReflectionAtolFptrRuntime(instance, fptrType, constantsType);
        }

        private static Exception Unwrap(TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
    }

    internal sealed class ReflectionAtolFptrRuntime : IAtolFptrRuntime
    {
        private readonly object _instance;
        private readonly Type _fptrType;
        private readonly Type _constantsType;

        internal ReflectionAtolFptrRuntime(object instance, Type fptrType, Type constantsType)
        {
            _instance = instance;
            _fptrType = fptrType;
            _constantsType = constantsType;
        }

        public void Configure(string portName, bool autoReconnect)
        {
            SetSingleSetting("LIBFPTR_SETTING_MODEL", ConstantInt("LIBFPTR_MODEL_ATOL_AUTO").ToString());
            SetSingleSetting("LIBFPTR_SETTING_PORT", ConstantInt("LIBFPTR_PORT_COM").ToString());
            SetSingleSetting("LIBFPTR_SETTING_COM_FILE", portName);
            SetSingleSetting("LIBFPTR_SETTING_BAUDRATE", ConstantInt("LIBFPTR_PORT_BR_115200").ToString());
            SetSingleSetting("LIBFPTR_SETTING_AUTO_RECONNECT", autoReconnect ? "true" : "false");
            EnsureSuccess("applySingleSettings", InvokeInt("applySingleSettings"));
        }

        public void Open()
        {
            EnsureSuccess("open", InvokeInt("open"));
            object opened = Invoke("isOpened", Type.EmptyTypes, new object[0]);
            if (!(opened is bool) || !(bool)opened)
                throw DriverError("ККТ не перешла в состояние «открыта»");
        }

        public KktConnectionIdentity ReadIdentity()
        {
            Query("LIBFPTR_DT_SERIAL_NUMBER");
            string serial = GetParamString("LIBFPTR_PARAM_SERIAL_NUMBER");
            Query("LIBFPTR_DT_MODEL_INFO");
            string model = GetParamString("LIBFPTR_PARAM_MODEL_NAME");

            SetParam("LIBFPTR_PARAM_DATA_TYPE", ConstantInt("LIBFPTR_DT_UNIT_VERSION"));
            SetParam("LIBFPTR_PARAM_UNIT_TYPE", ConstantInt("LIBFPTR_UT_FIRMWARE"));
            EnsureSuccess("queryData", InvokeInt("queryData"));
            string version = GetParamString("LIBFPTR_PARAM_UNIT_VERSION");
            string release = GetOptionalParamString("LIBFPTR_PARAM_UNIT_RELEASE_VERSION");
            if (!string.IsNullOrWhiteSpace(release) &&
                !string.Equals(version, release, StringComparison.OrdinalIgnoreCase))
            {
                version = string.IsNullOrWhiteSpace(version) ? release : version + "." + release;
            }

            return new KktConnectionIdentity
            {
                KktSerial = serial == null ? string.Empty : serial.Trim(),
                ModelName = model == null ? string.Empty : model.Trim(),
                FirmwareVersion = version == null ? string.Empty : version.Trim()
            };
        }

        public void Close()
        {
            object opened = Invoke("isOpened", Type.EmptyTypes, new object[0]);
            if (opened is bool && (bool)opened) InvokeInt("close");
        }

        public void Destroy()
        {
            Invoke("destroy", Type.EmptyTypes, new object[0]);
        }

        private void Query(string dataTypeConstant)
        {
            SetParam("LIBFPTR_PARAM_DATA_TYPE", ConstantInt(dataTypeConstant));
            EnsureSuccess("queryData", InvokeInt("queryData"));
        }

        private void SetSingleSetting(string settingConstant, string value)
        {
            string setting = ConstantString(settingConstant);
            Invoke("setSingleSetting", new Type[] { typeof(string), typeof(string) },
                new object[] { setting, value });
        }

        private void SetParam(string parameterConstant, int value)
        {
            Invoke("setParam", new Type[] { typeof(int), typeof(int) },
                new object[] { ConstantInt(parameterConstant), value });
        }

        private string GetParamString(string parameterConstant)
        {
            object result = Invoke("getParamString", new Type[] { typeof(int) },
                new object[] { ConstantInt(parameterConstant) });
            return result as string;
        }

        private string GetOptionalParamString(string parameterConstant)
        {
            try { return GetParamString(parameterConstant); }
            catch { return string.Empty; }
        }

        private int ConstantInt(string name)
        {
            object value = Constant(name);
            return Convert.ToInt32(value);
        }

        private string ConstantString(string name)
        {
            object value = Constant(name);
            return Convert.ToString(value);
        }

        private object Constant(string name)
        {
            FieldInfo field = _constantsType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new InvalidOperationException("В установленном API АТОЛ нет ожидаемой константы.");
            return field.GetValue(null);
        }

        private int InvokeInt(string methodName)
        {
            object value = Invoke(methodName, Type.EmptyTypes, new object[0]);
            return Convert.ToInt32(value);
        }

        private object Invoke(string methodName, Type[] parameterTypes, object[] arguments)
        {
            MethodInfo method = _fptrType.GetMethod(methodName, parameterTypes);
            if (method == null) throw new InvalidOperationException("Установленный API АТОЛ не совместим с этой версией программы.");
            try
            {
                return method.Invoke(_instance, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private void EnsureSuccess(string operation, int result)
        {
            if (result < 0) throw DriverError(operation);
        }

        private Exception DriverError(string operation)
        {
            int code = 0;
            try { code = InvokeInt("errorCode"); }
            catch { }
            return new InvalidOperationException(
                "Драйвер АТОЛ не выполнил операцию " + operation +
                " (код " + code + "). Закройте Frontol/«Тест драйвера ККТ» и повторите.");
        }
    }
}
