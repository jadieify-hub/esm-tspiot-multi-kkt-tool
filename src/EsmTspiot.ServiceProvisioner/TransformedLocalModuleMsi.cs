using System;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class TransformedLocalModuleMsi : IDisposable
    {
        internal TransformedLocalModuleMsi(
            string fullPath,
            LocalModuleMsiTransformPlan plan,
            FileStream outputLock)
        {
            FullPath = fullPath;
            Plan = plan;
            _outputLock = outputLock;
        }

        private FileStream _outputLock;

        internal string FullPath { get; private set; }
        internal LocalModuleMsiTransformPlan Plan { get; private set; }

        public void Dispose()
        {
            if (_outputLock != null)
            {
                _outputLock.Dispose();
                _outputLock = null;
            }
        }
    }
}
