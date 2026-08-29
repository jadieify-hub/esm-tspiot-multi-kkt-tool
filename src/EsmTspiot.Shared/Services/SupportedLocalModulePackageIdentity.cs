using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class SupportedLocalModulePackageIdentity
    {
        public const string FileName = "regime-2.6.1-7.msi";
        public const long ByteLength = 51007488;
        public const string Sha256 =
            "68a9633cefc912c2c1defae40d1c8f433bb794ee66060b822f0895410ab6c5c6";
        public const string ProductName = "Локальный модуль Честный Знак";
        public const string ProductVersion = "2.6.1";
        public const string ProductCode =
            "{556FD8AD-43A3-4645-BC54-EBF3043ADF82}";
        public const string UpgradeCode =
            "{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}";
        public const string SignerSubject =
            "CN=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ, " +
            "O=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ, " +
            "STREET=\"ул Рочдельская, 15 / строение 16а\", L=Москва, S=Москва, C=RU, " +
            "OID.1.3.6.1.4.1.311.60.2.1.2=Moscow, OID.1.3.6.1.4.1.311.60.2.1.3=RU, " +
            "SERIALNUMBER=1177746542247, OID.2.5.4.15=Private Organization";
        public const string SignerThumbprint =
            "6BA5F6BBE4BE27658253C78889334D0E24858C19";

        public static LocalModuleInstallerSelection Create(
            string sourcePath,
            bool licenseNoticeAccepted)
        {
            return new LocalModuleInstallerSelection
            {
                SourcePath = sourcePath ?? string.Empty,
                FileName = FileName,
                ByteLength = ByteLength,
                Sha256 = Sha256,
                ProductName = ProductName,
                ProductVersion = ProductVersion,
                ProductCode = ProductCode,
                UpgradeCode = UpgradeCode,
                SignerSubject = SignerSubject,
                SignerThumbprint = SignerThumbprint,
                LicenseNoticeAccepted = licenseNoticeAccepted
            };
        }
    }
}
