using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using System.IO;
using EsmTspiot.WinForms.Shared;

namespace EsmTspiot.Operator.Tests
{
    internal static class OrganizationTests
    {
        internal static void Run()
        {
            var driver = new Driver();
            var runtime = new ReflectionAtolFptrRuntime(driver, typeof(Driver), typeof(Constants));
            KktConnectionIdentity identity = runtime.ReadIdentity();
            var name = typeof(KktConnectionIdentity).GetProperty("OrganizationName");
            var inn = typeof(KktConnectionIdentity).GetProperty("OrganizationInn");
            if (name == null || inn == null)
                throw new Exception("The ATOL identity must include optional organization name and registration INN.");
            if ((string)name.GetValue(identity, null) != "Общество с ограниченной ответственностью «Пример»" ||
                (string)inn.GetValue(identity, null) != "1234567894")
                throw new Exception("Organization must come from FN registration tags 1048 and 1018.");
            driver.FailName = true;
            identity = runtime.ReadIdentity();
            if (identity.KktSerial != "00105700000001" || !string.IsNullOrEmpty((string)name.GetValue(identity, null)))
                throw new Exception("Optional organization failure must preserve the KKT identity.");
            string path = Path.Combine(Path.GetTempPath(), "MultiKKT-organization-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new KktOrganizationStore(path);
                store.Remember("00105700000001", "1234567894", "Индивидуальный предприниматель Иванов Иван Иванович");
                store.Save();
                store = new KktOrganizationStore(path);
                if (KktOrganizationStore.DisplayName(store.GetName("00105700000001", "1234567894")) != "ИП Иванов Иван Иванович" ||
                    store.GetName("00105700000001", "7701234560") != string.Empty ||
                    KktOrganizationStore.DisplayName("Общество с ограниченной ответственностью «Пример»") != "ООО «Пример»" ||
                    KktOrganizationStore.DisplayName("ООО «Пример»") != "ООО «Пример»")
                    throw new Exception("Organization cache must round-trip, match INN, and shorten only legal forms.");
                File.WriteAllText(path, "broken");
                if (new KktOrganizationStore(path).GetName("00105700000001", "1234567894") != string.Empty)
                    throw new Exception("Unreadable optional metadata must not invent an organization.");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        public sealed class Driver
        {
            private readonly Dictionary<int, int> _parameters = new Dictionary<int, int>();
            public bool FailName;
            public void setParam(int key, int value) { _parameters[key] = value; }
            public int queryData() { return 0; }
            public int fnQueryData()
            {
                if (_parameters[Constants.LIBFPTR_PARAM_FN_DATA_TYPE] != Constants.LIBFPTR_FNDT_REG_INFO)
                    throw new Exception("Only reading current registration information is allowed.");
                if (FailName) return -1;
                return 0;
            }
            public int errorCode() { return 11; }
            public string getParamString(int key)
            {
                if (key == Constants.LIBFPTR_PARAM_SERIAL_NUMBER) return "00105700000001";
                if (key == Constants.LIBFPTR_PARAM_MODEL_NAME) return "АТОЛ 30Ф";
                if (key == Constants.LIBFPTR_PARAM_UNIT_VERSION) return "5.16";
                if (key == 1018) return "1234567894";
                if (key == 1048) return "Общество с ограниченной ответственностью «Пример»";
                return string.Empty;
            }
        }

        public static class Constants
        {
            public const int LIBFPTR_PARAM_DATA_TYPE = 1, LIBFPTR_DT_SERIAL_NUMBER = 2,
                LIBFPTR_PARAM_SERIAL_NUMBER = 3, LIBFPTR_DT_MODEL_INFO = 4,
                LIBFPTR_PARAM_MODEL_NAME = 5, LIBFPTR_DT_UNIT_VERSION = 6,
                LIBFPTR_PARAM_UNIT_TYPE = 7, LIBFPTR_UT_FIRMWARE = 8,
                LIBFPTR_PARAM_UNIT_VERSION = 9, LIBFPTR_PARAM_UNIT_RELEASE_VERSION = 10,
                LIBFPTR_PARAM_FN_DATA_TYPE = 11, LIBFPTR_FNDT_TAG_VALUE = 12,
                LIBFPTR_PARAM_TAG_NUMBER = 13, LIBFPTR_PARAM_TAG_VALUE = 14, LIBFPTR_FNDT_REG_INFO = 15;
        }
    }
}
