using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZetaLongPaths;

namespace KookooReplace
{
    class Program
    {
        static void Main(string[] args)
        {
            string pathTo7Zip = Path.Combine(Path.GetTempPath(), "7zipa.exe");
            File.WriteAllBytes(pathTo7Zip, Resource._7za);

            var fileNameOfPayload = String.Empty;

            if (args.Length < 1 || !File.Exists(args[0]))
            {
                Console.WriteLine(
                    "I need a file name of the file to replace found files with...Please restart with the file name provided.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            fileNameOfPayload = args[0];

            var archiveExtension = String.Empty;

            if (args.Length > 1)
            {
                archiveExtension = args[1];
            }

            var payloadFileExistis = File.Exists(fileNameOfPayload);
            var currentDirectory = Directory.GetCurrentDirectory();


            if (!payloadFileExistis)
            {
                Console.WriteLine(
                    $"Can't find file to replace occurences with. {fileNameOfPayload} is not in the current folder {currentDirectory}.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            var filePathToPayload =
                Directory.GetFiles(currentDirectory, fileNameOfPayload, SearchOption.TopDirectoryOnly).FirstOrDefault();

            var allDirectoriesToSearchForFilesToReplace = Directory.GetDirectories(currentDirectory, "*",
                SearchOption.TopDirectoryOnly);

            var archivePattern = String.IsNullOrEmpty(archiveExtension) ? "*.zip" : "*." + archiveExtension;

            foreach (var directory in allDirectoriesToSearchForFilesToReplace)
            {
                Console.WriteLine($"---Processing directory {directory}");

                //var filesToReplaceInCurrentDirAndDown = Directory.GetFiles(directory, fileNameOfPayload,
                //    SearchOption.AllDirectories);

                var dirObject = new ZlpDirectoryInfo(directory);

                var filesToReplaceInCurrentDirAndDown = dirObject.GetFiles(fileNameOfPayload, SearchOption.AllDirectories);

                foreach (var fileToReplace in filesToReplaceInCurrentDirAndDown)
                {
                    //bool alreadyReplaced = new FileInfo(fileToReplace).Length == new FileInfo(filePathToPayload).Length &&
                    //                       File.ReadAllBytes(fileToReplace)
                    //                           .SequenceEqual(File.ReadAllBytes(filePathToPayload));

                    bool alreadyReplaced = fileToReplace.Length == new FileInfo(filePathToPayload).Length &&
                                           fileToReplace.ReadAllBytes()
                                               .SequenceEqual(File.ReadAllBytes(filePathToPayload));

                    if (!alreadyReplaced)
                    {
                        Console.WriteLine($"---------Found file {fileToReplace}. Replacing...");
                        File.Copy(filePathToPayload, fileToReplace.OriginalPath, true);
                    }
                }

                //var archivesToSearchInCurrentDirAndDown = Directory.GetFiles(directory, archivePattern,
                //    SearchOption.AllDirectories);

                var archivesToSearchInCurrentDirAndDown = dirObject.GetFiles(archivePattern, SearchOption.AllDirectories);

                foreach (var archiveToSearch in archivesToSearchInCurrentDirAndDown)
                {
                    Console.WriteLine($"------Processing archive {archiveToSearch}");
                    List<string> filesToUpdate = new List<string>();

                    using (ZipArchive zip = ZipFile.Open(archiveToSearch.OriginalPath, ZipArchiveMode.Read))
                    {
                        for (int i = 0; i < zip.Entries.Count; i++)
                        {
                            var entry = zip.Entries[i];

                            if (entry.Name == fileNameOfPayload)
                            {
                                filesToUpdate.Add(entry.FullName);
                            }
                        }
                    }

                    if (filesToUpdate.Count > 0)
                    {
                        try
                        {
                            using (ZipArchive zip = ZipFile.Open(archiveToSearch.OriginalPath, ZipArchiveMode.Update))
                            {
                                foreach (var fileToUpdate in filesToUpdate)
                                {
                                    Console.WriteLine($"---------Found file {archiveToSearch} {fileToUpdate}. Replacing...");
                                    zip.Entries.Single(e => e.FullName == fileToUpdate).Delete();
                                    zip.CreateEntryFromFile(filePathToPayload, fileToUpdate, CompressionLevel.NoCompression);
                                }
                            }
                        }
                        catch (IOException)
                        {
                            Console.WriteLine($"------Can't update archive {archiveToSearch}. File is locked.");
                        }

                    }

                    Console.WriteLine("------Finished processing archive.");
                }


                Console.WriteLine("---Finished processing directory.");
            }

            Console.WriteLine("Removing 7zip...");

            File.Delete(pathTo7Zip);

            Console.WriteLine("Done. Exiting...");
            Environment.Exit(0);
        }

    }

}
