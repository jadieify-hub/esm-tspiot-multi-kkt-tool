using System;
using System.Collections.Generic;
using System.IO;

namespace EsmTspiot.Shared.Services
{
    public static class InstructionFileSelector
    {
        public static string SelectAvailable(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return string.Empty;
            }

            string bundledGuide = Path.Combine(directory, "INSTRUCTION_FOR_DUMMIES.txt");
            if (File.Exists(bundledGuide))
            {
                return bundledGuide;
            }

            string[] files = Directory.GetFiles(directory, "Instruction-MultiKKT*.pdf");
            if (files.Length == 0)
            {
                files = Directory.GetFiles(directory, "instrukciya_dlya_chaynikov*.pdf");
            }
            if (files.Length == 0)
            {
                files = Directory.GetFiles(directory, "FIELD_TEST*.md");
            }
            return SelectNewest(files);
        }

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
