using System;
using System.Collections.Generic;
using System.IO;

namespace EsmTspiot.Shared.Services
{
    public static class InstructionFileSelector
    {
        public static string SelectNewest(IList<string> files)
        {
            string selected = string.Empty;
            DateTime selectedWriteTime = DateTime.MinValue;
            if (files == null)
            {
                return selected;
            }

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                DateTime writeTime = File.GetLastWriteTime(file);
                if (string.IsNullOrEmpty(selected) || writeTime > selectedWriteTime)
                {
                    selected = file;
                    selectedWriteTime = writeTime;
                }
            }

            return selected;
        }
    }
}
