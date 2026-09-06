using System;
using System.Threading;
using System.Threading.Tasks;

namespace EsmTspiot.Shared.Services
{
    public enum LmAutomaticSetupStage
    {
        Registration = 0,
        // ЛМ ЧЗ поднимается до контроллеров: контроллер ищет свой локальный
        // модуль при старте и, не найдя, отвечает ЕСМ «ЛМ Контроллер не смог
        // найти ЛМ ЧЗ», а сама привязка отвергается кодом 2025.
        LocalModuleEnsure = 1,
        ControllerEnsure = 2,
        EsmBinding = 3,
        InitializationDeferred = 5
    }

    public sealed class LmAutomaticSetupResult
    {
        public bool FmuApiMode { get; internal set; }
        public bool RegistrationSucceeded { get; internal set; }
        public bool ControllerEnsureSucceeded { get; internal set; }
        public bool LocalModuleEnsureSucceeded { get; internal set; }
        public bool EsmBindingSucceeded { get; internal set; }
        public bool InitializationDeferred { get; internal set; }

        public bool LocalModuleDeferred
        {
            get { return !LocalModuleEnsureSucceeded; }
        }

        // FMU mode completes preparation only: registration and local modules.
        // The standard contour additionally requires controllers and accepted
        // ESM settings. LM readiness is checked only in manual diagnostics.
        public bool Complete
        {
            get
            {
                return RegistrationSucceeded &&
                    LocalModuleEnsureSucceeded &&
                    (FmuApiMode || (ControllerEnsureSucceeded &&
                        EsmBindingSucceeded));
            }
        }

        public string IncompleteReason
        {
            get
            {
                if (!RegistrationSucceeded)
                    return "регистрация ККТ не завершена";
                if (!LocalModuleEnsureSucceeded)
                    return "ЛМ ЧЗ не установлены или отложены";
                if (FmuApiMode)
                    return string.Empty;
                if (!ControllerEnsureSucceeded)
                    return "не все контроллеры готовы";
                if (!EsmBindingSucceeded)
                    return "настройки привязки к ЕСМ не применены";
                return string.Empty;
            }
        }
    }

    public sealed class LmAutomaticSetupCoordinator
    {
        public async Task<bool> ExecuteAsync(
            Func<CancellationToken, Task<bool>> install,
            Func<CancellationToken, Task<bool>> configure,
            CancellationToken cancellation)
        {
            if (install == null)
            {
                throw new ArgumentNullException("install");
            }
            if (configure == null)
            {
                throw new ArgumentNullException("configure");
            }

            cancellation.ThrowIfCancellationRequested();
            if (!await install(cancellation))
            {
                return false;
            }

            cancellation.ThrowIfCancellationRequested();
            return await configure(cancellation);
        }

        public async Task<LmAutomaticSetupResult> ExecuteFullAsync(
            Func<CancellationToken, Task<bool>> register,
            Func<CancellationToken, Task<bool>> ensureLocalModules,
            Func<CancellationToken, Task<bool>> ensureControllers,
            Func<CancellationToken, Task<bool>> bindEsm,
            Action<LmAutomaticSetupStage> reportStage,
            CancellationToken cancellation,
            bool fmuApiMode = false)
        {
            if (register == null) throw new ArgumentNullException("register");
            if (ensureLocalModules == null)
                throw new ArgumentNullException("ensureLocalModules");
            if (ensureControllers == null)
                throw new ArgumentNullException("ensureControllers");
            if (bindEsm == null) throw new ArgumentNullException("bindEsm");

            LmAutomaticSetupResult result = new LmAutomaticSetupResult
            {
                FmuApiMode = fmuApiMode
            };
            Report(reportStage, LmAutomaticSetupStage.Registration);
            cancellation.ThrowIfCancellationRequested();
            result.RegistrationSucceeded = await register(cancellation);
            if (!result.RegistrationSucceeded) return result;

            Report(reportStage, LmAutomaticSetupStage.LocalModuleEnsure);
            cancellation.ThrowIfCancellationRequested();
            result.LocalModuleEnsureSucceeded =
                await ensureLocalModules(cancellation);

            if (fmuApiMode)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            else
            {
                Report(reportStage, LmAutomaticSetupStage.ControllerEnsure);
                cancellation.ThrowIfCancellationRequested();
                result.ControllerEnsureSucceeded =
                    await ensureControllers(cancellation);

                Report(reportStage, LmAutomaticSetupStage.EsmBinding);
                cancellation.ThrowIfCancellationRequested();
                result.EsmBindingSucceeded = await bindEsm(cancellation);

            }

            cancellation.ThrowIfCancellationRequested();
            Report(reportStage, LmAutomaticSetupStage.InitializationDeferred);
            result.InitializationDeferred = true;
            return result;
        }

        private static void Report(
            Action<LmAutomaticSetupStage> reportStage,
            LmAutomaticSetupStage stage)
        {
            if (reportStage != null) reportStage(stage);
        }
    }
}
