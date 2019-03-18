using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using CommandLine;
using CommandLine.Text;
using ICSharpCode.SharpZipLib.Zip;
using ZetaLongPaths;

namespace KookooReplace
{
    class Program
    {
        public enum Mode
        {
            Folder = 1,
            SQLImage = 2
        }

        public class Options
        {
            [Option('r',
                "rootDirectory",
                Default = ".",
                Required = false,
                HelpText = "Defines a directory which serves as a root for recursive \"deep dive\" to find files to replace with etalon file.")]
            public string RootDirectory { get; set; }

            [Option('u',
                "uniqueID",
                Required = false,
                HelpText = "Defines a unique ID of the table where the file replacement is happening in case of SQLImage mode is engaged.")]
            public string IdColumn { get; set; }

            [Option('f',
                "fileToReplaceWith",
                Required = true,
                HelpText = "A file path or a name in case located in the current folder of the etalon file that the other files with the same name are recursivelly replaced.")]
            public string File { get; set; }

            [Option('m',
                "mode",
                Required = false,
                HelpText = "Kookoo Replace can operate either in folder or sql image mode to replace file in folder or archive. Folder mode is assumed default.",
                Default = Mode.Folder)]
            public Mode Mode { get; set; }

            // Omitting long name, defaults to name of property, ie "--verbose"
            [Option('a', "archive",
                Required = false,
                HelpText = "Defines which type of archives will Kookoo Replace look into to find files to replace. Can be a | separated array of extensions: jar|orb|zip.",
                Separator = '|')]
            public IEnumerable<string> ArchiveExtensions { get; set; }

            [Option('t',
                "Table",
                Required = false,
                Default = " ",
                HelpText = "Defines a table name from which the images will be extracted for procession. If this parameter is specified, 'f' and 'i' params must also be specified.")]
            public string Table { get; set; }

            [Option('F',
                "fileNameColumn",
                Required = false,
                HelpText = "Defines a column name where a script can find a file name to materialize the image to for futher procession.",
                Default = " "
                )]
            public string FileNameColumn { get; set; }


            [Option('i',
                "imageColumn",
                Required = false,
                HelpText = "Defines a column name where a script can find an image to materialize the image to for futher procession.",
                Default = " "
            )]
            public string ImageColumn { get; set; }

            [Option('c',
                "connectionString",
                Required = false,
                HelpText = "In case SQL image mode is used, a connection string to a database where the target images are located.")]
            public string ConnectionString { get; set; }
        }

        public class UpdateRecord
        {
            public string Id { get; set; }
            public string FilePath { get; set; }
        }

        static void Main(string[] args)
        {
            var parser = new Parser(settings =>
            {
                settings.CaseInsensitiveEnumValues = true;
                settings.HelpWriter = Console.Out;
            });

            parser.ParseArguments<Options>(args)
                .WithParsed<Options>(o =>
                {
                    if (o.Mode == Mode.Folder) RunFolderMode(o);
                    else RunSQLMode(o);
                }).WithNotParsed(OutputErrorsAndExit);


        }

        private static void OutputErrorsAndExit(IEnumerable<Error> errors)
        {
            Console.WriteLine("Error parsing arguments. Details below:");
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            Environment.Exit(1);
        }

        public static void RunSQLMode(Options o)
        {
            var payloadFilePath = Path.GetFullPath(o.File);
            var updateList = new List<UpdateRecord>();

            var payloadFileExistis = File.Exists(payloadFilePath);

            if (!payloadFileExistis)
            {
                Console.WriteLine(
                    $"Can't find file to replace occurences with. {payloadFilePath} does not point to an existing file.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            var fileNameOfPayload = Path.GetFileName(payloadFilePath);

            if (String.IsNullOrEmpty(o.ConnectionString) || string.IsNullOrEmpty(o.FileNameColumn) ||
                string.IsNullOrEmpty(o.ImageColumn) || string.IsNullOrEmpty(o.Table) || string.IsNullOrEmpty(o.IdColumn))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("When selecting SQLImage mode, the following parameters must be specified: -f, -F, -t, -c, -i, -u and an -a optionally. Please make sure you speficy all.");
                Console.WriteLine("Exiting...");
                Environment.Exit(1);
            }

            if (!String.IsNullOrEmpty(o.RootDirectory))
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("-r paramter value is ignored when operating in an SQLImage mode.");
                Console.WriteLine("Proceeding...");
                Console.ForegroundColor = originalColor;
            }

            using (var connection = new SqlConnection(
                o.ConnectionString
            ))
            {
                var workingDir = GetTempDirectory();

                try
                {
                    connection.Open();
                    Console.WriteLine("Connected successfully.");

                    string selectStatement = $"SELECT {o.IdColumn},{o.FileNameColumn},{o.ImageColumn} FROM {o.Table}";
                    string updateStatement = @"UPDATE @table
                    SET @dataColumn = @data
                    WHERE @idColumn = @id";
                    updateStatement = updateStatement.Replace("@table", o.Table);
                    updateStatement = updateStatement.Replace("@idColumn", o.IdColumn);
                    updateStatement = updateStatement.Replace("@dataColumn", o.ImageColumn);

                    using (var selectCommand = new SqlCommand(selectStatement, connection))
                    {
                        selectCommand.CommandTimeout = 0;
                        using (var reader = selectCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var currentFileName = reader[o.FileNameColumn];
                                var currentFilePath = workingDir + "\\" + currentFileName;
                                var currentId = reader[o.IdColumn].ToString();

                                Console.WriteLine($"Processing {currentFileName}...");
                                var bytes = (Byte[]) reader[o.ImageColumn];
                                FileStream fs = new FileStream(
                                    currentFilePath,
                                    FileMode.OpenOrCreate);
                                fs.Write(bytes, 0, bytes.Length);
                                fs.Close();
                                Console.WriteLine("File is written to a temp directory for procession...");

                                //UPDATE FILE
                                if (Equals(currentFileName,fileNameOfPayload))
                                {
                                    var originalColor = Console.ForegroundColor;
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine("File extracted from DB is named the same as the etalon file. Direct replacement commencing...");
                                    File.Copy(payloadFilePath, currentFilePath, true);
                                    Console.WriteLine("Target file rewritten.");
                                    Console.ForegroundColor = originalColor;

                                    updateList.Add(new UpdateRecord {Id = currentId, FilePath = currentFilePath});
                                }
                                else if (o.ArchiveExtensions.Contains(Path.GetExtension(currentFilePath).Remove(0,1)))
                                {
                                    //Dive into archive to search for a payload to replace with.
                                    Console.WriteLine($"------Processing archive {currentFilePath}");
                                    List<string> filesToUpdate = new List<string>();

                                    Console.WriteLine($"------Openning file to search for target entries...");

                                    var archivePath = new ZlpFileInfo(currentFilePath);
                                    var streamToFile = archivePath.OpenRead();
                                    using (ZipFile zip = new ZipFile(streamToFile))
                                    {
                                        for (int i = 0; i < zip.Count; i++)
                                        {
                                            var entry = zip[i];

                                            var testValue = entry.Name.LastIndexOf("/") >= 0
                                                ? entry.Name.Substring(entry.Name.LastIndexOf("/") + 1)
                                                : entry.Name;

                                            if (testValue == fileNameOfPayload)
                                            {
                                                filesToUpdate.Add(entry.Name);
                                            }
                                        }
                                    }

                                    if (filesToUpdate.Count > 0)
                                    {
                                        streamToFile = archivePath.OpenWrite();
                                        var idealFileStream = File.OpenRead(payloadFilePath);

                                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                                        Console.WriteLine($"------Openning file to update found entries...");
                                        Console.ResetColor();

                                        try
                                        {
                                            foreach (var fileToUpdate in filesToUpdate)
                                            {
                                                UpdateZipInMemory(streamToFile, idealFileStream, fileToUpdate);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.BackgroundColor = ConsoleColor.Red;
                                            Console.WriteLine($"------Can't update archive {currentFilePath}. File is locked.");
                                            Console.WriteLine($"------payloadFilePath was {payloadFilePath}");
                                            Console.WriteLine(ex.Message);
                                            Console.WriteLine(ex.StackTrace);
                                            Console.WriteLine(ex.InnerException?.Message);
                                            Console.ResetColor();
                                            streamToFile.Close();
                                        }

                                        streamToFile.Close();

                                    }

                                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                                    Console.WriteLine("------Finished processing archive.");
                                    Console.ResetColor();

                                    updateList.Add(new UpdateRecord
                                    {
                                         Id = currentId,
                                         FilePath = currentFilePath
                                    });
                                }
                            }
                        }
                        //PUT IT BACK INTO DATABASE
                        foreach (var updateRecord in updateList)
                        {
                            var originalColor = Console.ForegroundColor;
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"Going to update {updateRecord.FilePath} in {updateRecord.Id} row...");

                            using (var updateCommand = new SqlCommand(updateStatement, connection))
                            {
                                updateCommand.CommandTimeout = 0;
                                updateCommand.Parameters.AddWithValue("@id", updateRecord.Id);
                                updateCommand.Parameters
                                        .Add("@data", SqlDbType.Image, GetFile(updateRecord.FilePath).Length).Value =
                                    GetFile(updateRecord.FilePath);

                                updateCommand.ExecuteNonQuery();
                            }

                            Console.WriteLine("Updated sucessfully. Proceeding to traverse the update list...");
                            Console.ForegroundColor = originalColor;
                        }
                    }

                    Directory.Delete(workingDir, true);
                }
                catch (Exception ex)
                {
                    Directory.Delete(workingDir, true);
                    var originalColor = Console.ForegroundColor;
                    Console.WriteLine($"Error has occured: \n {ex.Message}");
                    Console.WriteLine("Exiting...");
                    Environment.Exit(1);
                }
            }

        }

        public static byte[] GetFile(string filePath)
        {
            FileStream stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read);
            BinaryReader reader = new BinaryReader(stream);

            byte[] file = reader.ReadBytes((int)stream.Length);

            reader.Close();
            stream.Close();

            return file;
        }

        public static string GetTempDirectory()
        {
            string path = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());
            Directory.CreateDirectory(path);
            return path;
        }

        public static void RunFolderMode(Options o)
        {
            var payloadFilePath = Path.GetFullPath(o.File);

            var payloadFileExistis = File.Exists(payloadFilePath);
            var rootDirectory = o.RootDirectory;

            if (!payloadFileExistis)
            {
                Console.WriteLine(
                    $"Can't find file to replace occurences with. {payloadFilePath} does not point to an existing file.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            var fileNameOfPayload = Path.GetFileName(payloadFilePath);

            var allDirectoriesToSearchForFilesToReplace = Directory.GetDirectories(rootDirectory, "*",
                SearchOption.TopDirectoryOnly);

            if (File.Exists(rootDirectory + "\\" + fileNameOfPayload))
            {
                allDirectoriesToSearchForFilesToReplace = allDirectoriesToSearchForFilesToReplace
                    .Concat(new string[] {rootDirectory}).ToArray();
            }

            foreach (var directory in allDirectoriesToSearchForFilesToReplace)
            {
                Console.WriteLine($"---Processing directory {directory}");

                var dirObject = new ZlpDirectoryInfo(directory);

                var filesToReplaceInCurrentDirAndDown = dirObject.GetFiles(fileNameOfPayload, SearchOption.AllDirectories);

                foreach (var fileToReplace in filesToReplaceInCurrentDirAndDown)
                {
                    bool alreadyReplaced = fileToReplace.Length == new FileInfo(payloadFilePath).Length &&
                                           fileToReplace.ReadAllBytes()
                                               .SequenceEqual(File.ReadAllBytes(payloadFilePath));

                    if (!alreadyReplaced)
                    {
                        Console.WriteLine($"---------Found file {fileToReplace}. Replacing...");
                        File.Copy(payloadFilePath, fileToReplace.OriginalPath, true);
                    }
                }

                foreach (var archiveExtension in o.ArchiveExtensions)
                {
                    var archivesToSearchInCurrentDirAndDown =
                        dirObject.GetFiles("*." + archiveExtension, SearchOption.AllDirectories);

                    foreach (var archiveToSearch in archivesToSearchInCurrentDirAndDown)
                    {
                        Console.WriteLine($"------Processing archive {archiveToSearch}");
                        List<string> filesToUpdate = new List<string>();

                        Console.WriteLine($"------Openning file to search for target entries...");

                        var archivePath = new ZlpFileInfo(archiveToSearch.OriginalPath);
                        var streamToFile = archivePath.OpenRead();
                        using (ZipFile zip = new ZipFile(streamToFile))
                        {
                            for (int i = 0; i < zip.Count; i++)
                            {
                                var entry = zip[i];

                                var testValue = entry.Name.LastIndexOf("/") >= 0
                                    ? entry.Name.Substring(entry.Name.LastIndexOf("/") + 1)
                                    : entry.Name;

                                if (testValue == fileNameOfPayload)
                                {
                                    filesToUpdate.Add(entry.Name);
                                }
                            }
                        }

                        if (filesToUpdate.Count > 0)
                        {
                            streamToFile = archivePath.OpenWrite();
                            var idealFileStream = File.OpenRead(payloadFilePath);

                            Console.BackgroundColor = ConsoleColor.DarkGreen;
                            Console.WriteLine($"------Openning file to update found entries...");
                            Console.ResetColor();

                            try
                            {
                                foreach (var fileToUpdate in filesToUpdate)
                                {
                                    UpdateZipInMemory(streamToFile, idealFileStream, fileToUpdate);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.BackgroundColor = ConsoleColor.Red;
                                Console.WriteLine($"------Can't update archive {archiveToSearch}. File is locked.");
                                Console.WriteLine($"------payloadFilePath was {payloadFilePath}");
                                Console.WriteLine(ex.Message);
                                Console.WriteLine(ex.StackTrace);
                                Console.WriteLine(ex.InnerException?.Message);
                                Console.ResetColor();
                            }

                        }

                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.WriteLine("------Finished processing archive.");
                        Console.ResetColor();
                    }
                }


                Console.WriteLine("---Finished processing directory.");
            }

            Console.WriteLine("Done. Exiting...");
            Environment.Exit(0);
        }

        /// <summary>
        /// Updates a zip file (in a disk or memorystream) adding the entry contained in the second stream.
        /// </summary>
        /// <param name="zipStream">Zip file, could be a disk or memory stream. Must be seekable. </param>
        /// <param name="entryStream">Stream containing a file to be added. </param>
        /// <param name="entryName">Name to appear in the zip entry. </param>
        ///
        private static void UpdateZipInMemory(Stream zipStream, Stream entryStream, String entryName)
        {

            // The zipStream is expected to contain the complete zipfile to be updated
            ZipFile zipFile = new ZipFile(zipStream);

            zipFile.BeginUpdate();

            // To use the entryStream as a file to be added to the zip,
            // we need to put it into an implementation of IStaticDataSource.
            CustomStaticDataSource sds = new CustomStaticDataSource();
            sds.SetStream(entryStream);

            // If an entry of the same name already exists, it will be overwritten; otherwise added.
            zipFile.Add(sds, entryName);

            // Both CommitUpdate and Close must be called.
            zipFile.CommitUpdate();
            // Set this so that Close does not close the memorystream
            zipFile.IsStreamOwner = false;
            zipFile.Close();

            // Reposition to the start for the convenience of the caller.
            zipStream.Position = 0;
        }

        public class CustomStaticDataSource : IStaticDataSource
        {
            private Stream _stream;
            // Implement method from IStaticDataSource
            public Stream GetSource()
            {
                return _stream;
            }

            // Call this to provide the memorystream
            public void SetStream(Stream inputStream)
            {
                _stream = inputStream;
                _stream.Position = 0;
            }
        }

    }

}
