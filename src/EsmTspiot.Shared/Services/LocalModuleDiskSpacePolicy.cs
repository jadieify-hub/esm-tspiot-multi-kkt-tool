using System;

namespace EsmTspiot.Shared.Services
{
    public sealed class LocalModuleDiskSpaceProjection
    {
        public long InstallVolumeRequiredBytes { get; internal set; }
        public long SystemVolumeRequiredBytes { get; internal set; }
        public long InstallVolumeObservedFreeBytes { get; internal set; }
        public long SystemVolumeObservedFreeBytes { get; internal set; }

        public bool HasEnoughSpace
        {
            get
            {
                return InstallVolumeObservedFreeBytes >= InstallVolumeRequiredBytes &&
                    SystemVolumeObservedFreeBytes >= SystemVolumeRequiredBytes;
            }
        }
    }

    public static class LocalModuleDiskSpacePolicy
    {
        private const long Mebibyte = 1024L * 1024L;

        public static LocalModuleDiskSpaceProjection Evaluate(
            int newCloneCount,
            int newMsiCacheEntryCount,
            long installVolumeObservedFreeBytes,
            long systemVolumeObservedFreeBytes)
        {
            if (newCloneCount < 0) throw new ArgumentOutOfRangeException("newCloneCount");
            if (newMsiCacheEntryCount < 0)
            {
                throw new ArgumentOutOfRangeException("newMsiCacheEntryCount");
            }
            if (installVolumeObservedFreeBytes < 0)
            {
                throw new ArgumentOutOfRangeException("installVolumeObservedFreeBytes");
            }
            if (systemVolumeObservedFreeBytes < 0)
            {
                throw new ArgumentOutOfRangeException("systemVolumeObservedFreeBytes");
            }

            long installRequired = newCloneCount == 0
                ? 0
                : ((256L * newCloneCount) + 128L) * Mebibyte;
            long systemRequired = newMsiCacheEntryCount == 0
                ? 0
                : (384L + (64L * newMsiCacheEntryCount) + 128L) * Mebibyte;
            return new LocalModuleDiskSpaceProjection
            {
                InstallVolumeRequiredBytes = installRequired,
                SystemVolumeRequiredBytes = systemRequired,
                InstallVolumeObservedFreeBytes = installVolumeObservedFreeBytes,
                SystemVolumeObservedFreeBytes = systemVolumeObservedFreeBytes
            };
        }
    }
}
