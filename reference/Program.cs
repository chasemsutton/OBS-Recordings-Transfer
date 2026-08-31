using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Net.Mime;
using System.Net.Http;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Net.Http.Headers;

namespace Transfer_Recordings
{
    class Program
    {
        //config values and their defaults
        static double minFreeSpace = 25;//GB
        static int maxFileAge = 90;
        static string sourcePath = "";
        static string targetPath = "";
        static int autoCloseTimer = 15;

        static bool checkRemux = false;
        static bool checkTransfer = false;
        static string logFilesDetected = "";
        static string logFilesMoved = "";
        static string logFilesDeleted = "";
        static string logFilesLeft = "";
        static string[] targetFileNames;
        static string[] sourceFileNames;
        static string extensionDelete = ".mkv";
        static string extensionKeep = ".mp4";
        static List<string> failedHashes = new List<string>();
        static StreamWriter log;
        public static double dataToDelete;
        static void Main(string[] args)
        {
            //System.Diagnostics.Process process = new System.Diagnostics.Process();
            //System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            //{
            //    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            //    FileName = "cmd.exe",
            //    Arguments = "shutdown /s /t -0"
            //};
            //process.StartInfo = startInfo;
            //process.Start();
            //process.WaitForExit();
            try
            {

                //StreamWriter log; made global
                // If the directory already exists, this method does not create a new directory.
                if (!File.Exists("logfile.txt"))
                    log = new StreamWriter("logfile.txt");
                else
                    log = File.AppendText("logfile.txt");

                LoadConfigValues(System.IO.File.ReadAllLines(@"config.txt"));
                //string[] configFile = System.IO.File.ReadAllLines(@"config.txt");
                //sourcePath = configFile[0].Split('"')[1];
                //targetPath = configFile[1].Split('"')[1];
                //try
                //{
                //    maxFileAge = int.Parse(configFile[2].Split('"')[1]);
                //}
                //catch
                //{
                //    log.WriteLine("Max file age not provided. Defaulted to 90. Config File Modified.");
                //    System.IO.File.WriteAllLines(@"config.txt", configFile.ToList().Append("Max File Age: \"90\""));
                //}
                //try { minFreeSpace = double.Parse(configFile[3].Split('"')[1]); }
                //catch
                //{
                //    log.WriteLine("Minimum Free Space not provided. Defaulted to 25. Config File Modified.");
                //    System.IO.File.WriteAllLines(@"config.txt", configFile.ToList().Append("Minimum Free Space: \"25\""));
                //}


                if (Directory.Exists(sourcePath) && Directory.Exists(targetPath))
                {
                    FileInfo sourceFileInfo = new FileInfo(sourcePath);
                    string[] sourceFiles = Directory.GetFiles(sourcePath);                    
                    string[] targetFiles = Directory.GetFiles(targetPath);
                    sourceFileNames = new string[sourceFiles.Length];
                    targetFileNames = new string[targetFiles.Length];
                    for (int i = 0; i < sourceFiles.Length; i++)
                    {
                        sourceFileNames[i] = Path.GetFileName(sourceFiles[i]);
                    }
                    for (int i = 0; i < targetFiles.Length; i++)
                    {
                        targetFileNames[i] = Path.GetFileName(targetFiles[i]);
                    }
                    // mp4
                    List<string> existingMP4s = sourceFiles.ToList().Where(w => String.Compare(Path.GetExtension(Path.GetFileName(w)), extensionKeep) == 0).ToList();
                    // mkv
                    List<string> existingMKVs = sourceFiles.ToList().Where(w => String.Compare(Path.GetExtension(Path.GetFileName(w)), extensionDelete) == 0).ToList();

                    existingMP4s.ForEach(fe => LogDetectedFiles(fe));
                    existingMKVs.ForEach(fe => LogDetectedFiles(fe));

                    existingMP4s.ForEach(fe => ProcessRemuxedFile(fe));
                    existingMKVs.ForEach(fe => ProcessRawFile(fe));



                    //else { logFilesLeft = logFilesLeft + "\n" + fileName; }
                    // Copy the files and overwrite destination files if they already exist.

                    if (logFilesDetected == "")
                    {
                        log.WriteLine(DateTime.Now + " -------------------------------------------");
                        log.WriteLine("Files Detected-------------------------------------- \nNone \n");
                    }
                    else
                    {
                        log.WriteLine(DateTime.Now + " -------------------------------------------");
                        if (logFilesDetected != "") { log.WriteLine("Files Detected-----------------------------------------" + logFilesDetected + "\n"); }
                        else { log.WriteLine("Files Detected-------------------------------------- \nNone \n"); }
                        if (logFilesMoved != "") { log.WriteLine("Files Moved--------------------------------------" + logFilesMoved + "\n"); }
                        else { log.WriteLine("Files Moved------------------------------------- \nNone \n"); }
                        if (logFilesDeleted != "") { log.WriteLine("Files Deleted-----------------------------------" + logFilesDeleted + "\n"); }
                        else { log.WriteLine("Files Deleted----------------------------------- \nNone \n"); }
                        if (logFilesLeft != "") { log.WriteLine("Files Left-----------------------------------------" + logFilesLeft + "\n"); }
                        else { log.WriteLine("Files Left-------------------------------------- \nNone \n"); }
                    }
                }
                else
                {
                    log.WriteLine("------------------------------------------------------------");
                    log.WriteLine(DateTime.Now);
                    log.WriteLine("Source or target path did not exist!");
                }

                // Keep console window open in debug mode.
                log.Close();
            }
            catch(Exception ex)
            {
                StreamWriter err;
                if (!File.Exists("programError.txt"))
                {
                    err = new StreamWriter("programError.txt");
                }
                else
                {
                    err = File.AppendText("programError.txt");
                }
                err.WriteLine(DateTime.Now + " -------------------------------------------");
                err.WriteLine(ex.Message);
                err.Close();
            }
            Console.WriteLine("Completed all tasks...");
            Console.WriteLine("Closing in " + autoCloseTimer + " seconds... (configure auto close in config)");
            System.Threading.Thread.Sleep(autoCloseTimer * 1000);
            Environment.Exit(0);
        }

        private static void LoadConfigValues(string[] configLines)
        {
            var lines = configLines.ToList();

            sourcePath = LoadConfigLine(ref lines, "Source Path", sourcePath);
            targetPath = LoadConfigLine(ref lines, "Destination Path", targetPath);
            maxFileAge = int.Parse(LoadConfigLine(ref lines, "Max File Age (days)", maxFileAge.ToString()));
            minFreeSpace = double.Parse(LoadConfigLine(ref lines, "Minimum Free Space (GB)", minFreeSpace.ToString()));
            autoCloseTimer = int.Parse(LoadConfigLine(ref lines, "Auto Close Timer (seconds)", autoCloseTimer.ToString()));

            return;
        }
        private static string LoadConfigLine(ref List<string> configLines, string valueName, string defaultValue)
        {
            try
            {
                var stringValue = configLines.Where(w => w.Contains(valueName)).First();
                if (!String.IsNullOrWhiteSpace(stringValue))
                    return stringValue.Split('"')[1];
         
            }
            catch { }

            log.WriteLine(valueName + " not provided. Defaulted to " + defaultValue + ". Config File Modified.");
            configLines.Add(valueName + ": \"" + defaultValue + "\"");
            System.IO.File.WriteAllLines(@"config.txt", configLines);
            return defaultValue;

        }
        private static void ProcessRemuxedFile(string sourceFile)
        {

            var fileName = Path.GetFileName(sourceFile);
            if (Array.Exists(targetFileNames, element => element == fileName))
            {
                logFilesLeft = logFilesLeft + "\n" + fileName;
                return;
            }
            // Check if remux is complete
            string errorText;
            if (checkRemux) errorText = "abc";
            else errorText = "";
            while (!string.IsNullOrEmpty(errorText))
            {
                Console.WriteLine("Checking if video has been remuxed. This could take a while..");
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    FileName = "cmd.exe",
                    Arguments = "/C C:\\FFmpeg\\bin\\ffmpeg.exe -v error -i \"" + sourceFile + "\" -f null - > error.log 2>&1"
                };
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
                StreamReader errorLog = new StreamReader("error.log");
                errorText = errorLog.ReadToEnd();
                if (!string.IsNullOrEmpty(errorText))
                {
                    Console.WriteLine("Error found in video file.");
                }
            }

            // Transfer file and ensure transfer had no errors
            string response = "y";
            while (string.Compare(response.ToLower(), "y") == 0 || string.Compare(response.ToLower(), "yes") == 0)
            {
                string destFile = "";
                if (!Array.Exists(targetFileNames, element => element == fileName))
                {
                    destFile = Path.Combine(targetPath, fileName);
                    Console.WriteLine("Copying: " + fileName);
                    File.Copy(sourceFile, destFile);
                    Console.WriteLine("Copy Complete.");
                    logFilesMoved = logFilesMoved + "\n" + fileName;

                    File.Delete(sourceFile);
                    Console.WriteLine("Deleted: " + fileName);
                    if (!checkTransfer)
                        return;
                }
                else
                {
                    logFilesLeft = logFilesLeft + "\n" + fileName;
                    Console.WriteLine("File already exists at destination.");
                    if (!checkTransfer)
                        return;
                }
                if (checkTransfer)
                {
                    Console.WriteLine("Checking for errors in transfer.");
                    var sourceHash = Compute_MD5Hash_FromFile(sourceFile);
                    var destHash = Compute_MD5Hash_FromFile(destFile);
                    if (string.Compare(sourceHash, destHash) == 0)
                    {
                        Console.WriteLine("Copy Complete.");
                        logFilesMoved = logFilesMoved + "\n" + fileName;

                        File.Delete(sourceFile);
                        Console.WriteLine("Deleted: " + fileName);

                        if (failedHashes.Contains(sourceFile))
                            failedHashes.Remove(sourceFile);

                        response = "n";
                        break;
                    }
                    else
                    {
                        // error copying
                        Console.WriteLine("File transfer error: " + fileName);
                        if (!failedHashes.Contains(sourceFile))
                            failedHashes.Add(sourceFile);
                        Console.WriteLine("Would you like to try again? y/n");
                        response = Console.ReadLine();
                    }
                }
            }
        }

        private static void ProcessRawFile(string sourceFile)
        {
            //checking remaining drive space
            try
            {
                System.IO.DriveInfo SourceDrive = new System.IO.DriveInfo(sourceFile.Substring(0, 3));
                double remainingSpace = (SourceDrive.AvailableFreeSpace / 1073741824);
                if (remainingSpace < minFreeSpace) dataToDelete = minFreeSpace - remainingSpace;
                else dataToDelete = 0;
            }
            catch { dataToDelete = 0; }
            var fileName = Path.GetFileName(sourceFile);

            var createdDate = File.GetCreationTime(sourceFile);
            var fileAge = DateTime.Now - createdDate;
            if (fileAge.TotalDays > maxFileAge || (dataToDelete > 0 && fileAge.TotalDays > 8))
            {
                string fileNameKeepExtension = fileName.Substring(0, fileName.Length - extensionDelete.Length) + extensionKeep;
                if (Array.Exists(sourceFileNames, element => element == fileNameKeepExtension) || Array.Exists(targetFileNames, element => element == fileNameKeepExtension))
                {
                    File.Delete(sourceFile);
                    Console.WriteLine("Deleted: " + fileName);
                    logFilesDeleted = logFilesDeleted + "\n" + fileName;
                }
                
                else
                {
                    logFilesLeft = logFilesLeft + "\n" + fileName;
                }
            }
            else
            {
                logFilesLeft = logFilesLeft + "\n" + fileName;
            }
        }

        public static string Compute_MD5Hash_FromFile(string FileNameAndPath)
        {
            using (FileStream fs = new FileStream(FileNameAndPath,
            FileMode.Open))
            {
                return Convert.ToBase64String(new
                MD5CryptoServiceProvider().ComputeHash(fs));
            }
        }

        private static void LogDetectedFiles(string sourceFile)
        {
            logFilesDetected += "\n" + sourceFile.ToString();
        }

        private class ODBCFile
        {
            
        }
    //    private static enum ODBCFileState
    //    {
    //        public string fileName;
    //    string filePath;
    //}

}
}
