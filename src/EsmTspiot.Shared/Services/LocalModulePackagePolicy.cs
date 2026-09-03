using System;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// Мягкие пины пакета ЛМ ЧЗ: проверяется, что это пакет ЦРПТ (издатель
    /// сертификата и UpgradeCode), а не конкретная версия. Всё остальное
    /// подтверждается структурой самого MSI при подготовке клона.
    /// </summary>
    public static class LocalModulePackagePolicy
    {
        public static ValidationResult Evaluate(
            LocalModuleInstallerSelection selection)
        {
            ValidationResult result = new ValidationResult();
            if (selection == null)
            {
                result.Add("Не выбран MSI ЛМ ЧЗ.");
                return result;
            }
            if (!SupportedLocalModulePackageIdentity.MatchesSignerSubject(
                    selection.SignerSubject))
            {
                result.Add(
                    "MSI подписан не ЦРПТ. Наблюдаемый субъект: " +
                    Describe(selection.SignerSubject) + ".");
            }
            if (!SupportedLocalModulePackageIdentity.MatchesUpgradeCode(
                    selection.UpgradeCode))
            {
                result.Add(
                    "UpgradeCode пакета не совпадает с ЛМ ЧЗ. Ожидался " +
                    SupportedLocalModulePackageIdentity.UpgradeCode +
                    ", получен " + Describe(selection.UpgradeCode) + ".");
            }
            if (!string.Equals(
                    Trim(selection.ProductName),
                    SupportedLocalModulePackageIdentity.ProductName,
                    StringComparison.Ordinal))
            {
                result.AddWarning(
                    "Имя продукта в MSI отличается от известного: " +
                    Describe(selection.ProductName) + ".");
            }
            return result;
        }

        private static string Describe(string value)
        {
            string trimmed = Trim(value);
            return trimmed.Length == 0 ? "пусто" : trimmed;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
