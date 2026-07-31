using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace FoodDeliveryPrintingSystemLauncher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                byte[] payload;
                using (Stream stream = assembly.GetManifestResourceStream("Payload.zip"))
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    payload = memory.ToArray();
                }

                string hash;
                using (SHA256 sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "").Substring(0, 12);

                string runtimeRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FoodDeliveryPrintingSystem", "runtime", hash);
                string coreExe = Path.Combine(runtimeRoot, "FoodDeliveryPrintingSystem.Core.exe");

                if (!File.Exists(coreExe))
                {
                    Directory.CreateDirectory(runtimeRoot);
                    using (MemoryStream memory = new MemoryStream(payload))
                    using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Read))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destination = Path.GetFullPath(Path.Combine(runtimeRoot, entry.FullName));
                            if (!destination.StartsWith(runtimeRoot + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Invalid embedded file path.");
                            if (String.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(destination);
                                continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            entry.ExtractToFile(destination, true);
                        }
                    }
                }

                Process.Start(new ProcessStartInfo {
                    FileName = coreExe,
                    WorkingDirectory = runtimeRoot,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：\n" + ex.Message, "外卖打印系统",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
