using System;
using System.Threading;
using System.Threading.Tasks;

namespace EsmTspiot.Shared.Services
{
    public enum LmAutomaticSetupStage
    {
        Registration = 0,
        ControllerEnsure = 1,
        LocalModuleEnsure = 2,
        EsmBinding = 3,
        EsmReadback = 4,
        InitializationDeferred = 5
    }

    public sealed class LmAutomaticSetupResult
    {
        public bool RegistrationSucceeded { get; internal set; }
        public bool ControllerEnsureSucceeded { get; internal set; }
        public bool LocalModuleEnsureSucceeded { get; internal set; }
        public bool EsmBindingSucceeded { get; internal set; }
        public bool EsmReadbackSucceeded { get; internal set; }
        public bool InitializationDeferred { get; internal set; }

        public bool LocalModuleDeferred
        {
            get { return !LocalModuleEnsureSucceeded; }
        }

        // The contour counts as complete only when everything the cash desk
        // relies on after a reboot is confirmed: registration, controllers,
        // local modules, the ESM binding, and the ESM read-back of that
        // binding. LM business initialization is a separate deferred step.
        public bool Complete
        {
            get
            {
                return RegistrationSucceeded &&
                    ControllerEnsureSucceeded &&
                    LocalModuleEnsureSucceeded &&
                    EsmBindingSucceeded &&
                    EsmReadbackSucceeded;
            }
        }

        public string IncompleteReason
        {
            get
            {
                if (!RegistrationSucceeded)
                    return "регистрация ККТ не завершена";
                if (!ControllerEnsureSucceeded)
                    return "не все контроллеры готовы";
                if (!LocalModuleEnsureSucceeded)
                    return "ЛМ ЧЗ не установлены или отложены";
                if (!EsmBindingSucceeded)
                    return "привязка к ЕСМ не подтверждена";
                if (!EsmReadbackSucceeded)
                    return "ЕСМ не подтвердил привязку при контрольном чтении";
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
            Func<CancellationToken, Task<bool>> ensureControllers,
            Func<CancellationToken, Task<bool>> ensureLocalModules,
            Func<CancellationToken, Task<bool>> bindEsm,
            Func<CancellationToken, Task<bool>> readbackEsm,
            Action<LmAutomaticSetupStage> reportStage,
            CancellationToken cancellation)
        {
            if (register == null) throw new ArgumentNullException("register");
            if (ensureControllers == null)
                throw new ArgumentNullException("ensureControllers");
            if (ensureLocalModules == null)
                throw new ArgumentNullException("ensureLocalModules");
            if (bindEsm == null) throw new ArgumentNullException("bindEsm");
            if (readbackEsm == null) throw new ArgumentNullException("readbackEsm");

            LmAutomaticSetupResult result = new LmAutomaticSetupResult();
            Report(reportStage, LmAutomaticSetupStage.Registration);
            cancellation.ThrowIfCancellationRequested();
            result.RegistrationSucceeded = await register(cancellation);
            if (!result.RegistrationSucceeded) return result;

            Report(reportStage, LmAutomaticSetupStage.ControllerEnsure);
            cancellation.ThrowIfCancellationRequested();
            result.ControllerEnsureSucceeded =
                await ensureControllers(cancellation);

            Report(reportStage, LmAutomaticSetupStage.LocalModuleEnsure);
            cancellation.ThrowIfCancellationRequested();
            result.LocalModuleEnsureSucceeded =
                await ensureLocalModules(cancellation);

            Report(reportStage, LmAutomaticSetupStage.EsmBinding);
            cancellation.ThrowIfCancellationRequested();
            result.EsmBindingSucceeded = await bindEsm(cancellation);

            Report(reportStage, LmAutomaticSetupStage.EsmReadback);
            cancellation.ThrowIfCancellationRequested();
            result.EsmReadbackSucceeded = await readbackEsm(cancellation);

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
