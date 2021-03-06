﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using EdGo.EdgoClient;

namespace EdGo
{
    class FileProcessor
    {

		private String[] eventsArray = {"Bounty", "Docked", "FSDJump", "FetchRemoteModule", "LoadGame", "Location",
			"MaterialCollected", "MaterialDiscarded", "MaterialDiscovered", "MissionAbandoned", "MissionAccepted", "MissionCompleted",
			"MissionFailed", "ModuleBuy", "ModuleRetrieve", "ModuleSell", "ModuleStore", "ModuleSwap", "PowerplayCollect",
			"PowerplayDefect", "PowerplayDeliver", "PowerplayFastTrack", "PowerplayJoin", "PowerplayLeave", "PowerplaySalary", "PowerplayVote", "PowerplayVoucher",
			"Progress", "Rank", "ShipyardBuy", "ShipyardNew", "ShipyardSell", "ShipyardSwap"};

		IDictionary<String, byte> events = null;


		private Regex reg = new Regex("^.*/([^/]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private Regex regEvent = new Regex("^.*\"event\"\\:\"([^\"]+)\".*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private Regex regTimestamp = new Regex("^.*\"timestamp\"\\:\"([^\"]+)\".*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private Regex regCommander = new Regex("^.*\"Commander\"\\:\"([^\"]+)\".*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static string homeDir = Environment.ExpandEnvironmentVariables("%USERPROFILE%") + "\\Saved Games\\Frontier Developments\\Elite Dangerous\\";
        //private List<FileDescription> files = null;
		private StreamReader reader = null;
        //private CommandProcessor processor = null;
        TextLogger logger = TextLogger.instance;

		private String lastFile = null;
		private int lastLine = 0;
		public FileProcessor()
        {
        }

		public void load()
		{
			lastFile = Properties.Settings.Default.LastFile;
			lastLine = Properties.Settings.Default.LastLine;
		}

		public void saveSettings()
		{
			Properties.Settings.Default.LastFile = lastFile;
			Properties.Settings.Default.LastLine = lastLine;
		}

		private string time = null;
		private String lastEvent = null;
		private String hash = null;
		private String isNewCommander = null;

		public void setReset(String time = null, String lastEvent = null, String hash = null)
		{
			this.time = time;
			this.lastEvent = lastEvent;
			this.hash = hash;
		}
		public void resetTo() 
		{
			FileStream fs = null;
			StreamReader reader = null;

			try
			{
				String result = null;

				List<FileDescription> filelist = null;

				if (time == null)
				{
					filelist = directoryList(true);
					result = filelist[0].name;
					lastFile = result;
					lastLine = 0;
				}
				else
				{
					filelist = directoryList(false);
					int idx = 0;
					int count = filelist.Count;
					if (lastFile != null && lastFile.Length > 0)
					{
						for (int i = count - 1; i >= 0; i--)
						{
							logger.log("Find in file \"" + filelist[i].name + "\"");
							if (lastFile.Equals(filelist[i].name))
							{
								logger.log("Found \"" + filelist[i].name + "\"");
								idx = i;
								break;
							}
						}
					}
					else
					{
						idx = filelist.Count() - 1;
					}
					logger.log("Start index: " + idx);
					bool found = false;
					for (int i = idx; i >= 0 && !found; i--)
					{
						String fn = homeDir + filelist[i].name;
						logger.log("Scan file: " + filelist[i].name);
						int lineIdx = 0;
						using (fs = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
						{
							using (reader = new StreamReader(fs, System.Text.Encoding.UTF8))
							{
								String line = null;
								while ((line = reader.ReadLine()) != null)
								{
									Thread.Sleep(1);
									String timestamp = regTimestamp.Replace(line, "$1");
									if (timestamp.Equals(time))
									{
										String eventName = regEvent.Replace(line, "$1");
										if (eventName.Equals(lastEvent))
										{
											String md5 = CreateMD5(line.Trim()).ToLower();
											logger.log(md5);
											if (md5.Equals(hash))
											{
												found = true;
												result = filelist[i].name;
												lastFile = result;
												lastLine = lineIdx + 1;
												logger.log("Found file: " + result + "#Line:" + lineIdx);
												break;
											}
										}
									}
									lineIdx++;
								}
								reader.Close();
							}
							fs.Close();
						}
					}
					logger.log("Scan finish");
					saveSettings();
				}
				AppDispatcher.instance.endResetProcess();
			}
			catch (Exception e)
			{
				if (reader != null)
				{
					fs.Close();
				}
				if (fs != null)
				{
					fs.Close();
				}
			}
			//return result;
		}
		private List<FileDescription> directoryList(bool asc = true)
		{
			List<FileDescription> result = new List<FileDescription>();
			string[] filenames = Directory.GetFiles(homeDir, "*.log");
			foreach (string filename in filenames)
			{
				result.Add(new FileDescription(reg.Replace(filename.Replace('\\', '/'), "$1"), File.GetCreationTime(filename).ToString()));
			}

			if (asc)
			{
				result.Sort(compareByCreateTimeAsc);
			}
			else
			{
				result.Sort(compareByCreateTimeDesc);
			}

			return result;
		}


		public void processToLast()
		{
			try {
				//paused = true;
				List<FileDescription> filelist = directoryList();
				StringBuilder sb = new StringBuilder();
				bool skip = true;
				int i = filelist.Count() - 1;
				foreach (FileDescription fd in filelist)
				{
					if (skip)
					{
						if (fd.name.Equals(lastFile))
						{
							skip = false;
						}
					}

					if (!skip)
					{
						String fn = homeDir + fd.name;
						FileStream fs = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						reader = new StreamReader(fs, System.Text.Encoding.UTF8);
						for(int j = 0; j < lastLine; j++)
						{
							reader.ReadLine();
						}
						String line = null;
						while ((line = reader.ReadLine()) != null)
						{
							lastLine++;
							if (isProcess(line))
							{
								if (isNewCommander != null)
								{
									line = "{" + "\"IsNew\"=" + isNewCommander + ", " + line.Substring(2);
								}
								sb.Append(line);
							}
							if (sb.Length > 8192)
							{
								logger.log("Sending to server: " + sb.Length + " bytes");
								if (!processEvent(sb))
								{
									AppDispatcher.instance.responceError();
								}
								sb.Clear();
							}
						}
						lastLine = 0;
						if (i > 0)
						{
							reader.Close();
							fs.Close();
						}
					}
					i--;
				}
				if (sb.Length > 0)
				{
					logger.log("Sending to server last: " + sb.Length + " bytes");
					if (!processEvent(sb))
					{
						AppDispatcher.instance.responceError();
					}
				}
				AppDispatcher.instance.endReadToLastProcess();
				//paused = false;
			} catch (Exception e)
			{

			}
		}

		private bool paused = false;
		public void directoryRead()
        {
            try {
				//Regex reg = new Regex("^.*/([^/]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
				int lastCount = 0;
				while(true) {
					if (paused)
					{
						return;
						//Thread.Sleep(1000);
					} else { 
						List<FileDescription> filelist = directoryList(true);
						if (lastCount == 0)
						{
							lastCount = filelist.Count;
						}
						if (filelist.Count != lastCount)
						{
							lastLine = 0;
							lastCount = filelist.Count;
							bool skip = reader == null;
							reader = null;
							string fn = homeDir + filelist[filelist.Count - 1].name;
							logger.log("Using journal file: " + fn);
							FileStream fs = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
							reader = new StreamReader(fs, System.Text.Encoding.UTF8);
							if (skip)
							{
								reader.ReadToEnd();
							}

						}
						Thread.Sleep(1000);
					}
				}
            }
            catch (Exception e1)
            {
            }
        }

        private static int compareByCreateTimeAsc(FileDescription a, FileDescription b)
        {
            return File.GetCreationTime(homeDir + a.name).CompareTo(File.GetCreationTime(homeDir + b.name));
        }
		private static int compareByCreateTimeDesc(FileDescription a, FileDescription b)
		{
			return -File.GetCreationTime(homeDir + a.name).CompareTo(File.GetCreationTime(homeDir + b.name));
		}

		public void processCurrentFile()
        {
            try
            {
                while (true)
            {
                while (reader == null)
                {
                    Thread.Sleep(500);
                }

                string line = null;

				while ((line = reader.ReadLine()) != null)
				{
					lastLine++;
					if (isProcess(line))
					{
						if (isNewCommander != null)
						{
							line = "{" + "\"IsNew\"=" + isNewCommander + ", " + line.Substring(2);
						}
						logger.log("Read event line from journal file.");
						if (!processEvent(line))
						{
							AppDispatcher.instance.responceError();
						}
						AppDispatcher.instance.saveProperties();
					}
				}
                Thread.Sleep(500);
            }
            }
            catch (Exception e1)
            {
            }

        }
		private bool isProcess(String eventString) {
			if (events == null)
			{
				events = new Dictionary<String, byte>();
				foreach (String name in eventsArray)
				{
					events[name.ToLower()] = 0;
				}
			}
			isNewCommander = null;
			String eventName = regEvent.Replace(eventString, "$1");
			bool result = false;
			if (events.ContainsKey(eventName.ToLower()))
			{
				if (eventName.ToLower().Equals("loadgame"))
				{
					String commander = regCommander.Replace(eventString, "$1");
					paused = true;
					int res = AppDispatcher.instance.isNewPilot(commander);
					if (res != -1)
					{
						isNewCommander = res == 1 ? "true" : "false";
					} else
					{
						//AppDispatcher.instance.newPilotDialog(commander);
					}
				}
				result = true;
			}

			return result;
		}

		private bool processEvent(StringBuilder sb)
		{
			return processEvent(sb.ToString());
		}

		private bool processEvent(String sb)
		{
			bool result = false;
			try
			{
				IDictionary<String, object> response = Client.instance.sendEvent(sb);

				if (response.ContainsKey("result") && response["result"].Equals("OK"))
				{
					result = true;
				}
			}
			catch (Exception e)
			{

			}

			return result;
		}

		public static string CreateMD5(string input)
		{
			// Use input string to calculate MD5 hash
			using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
			{
				byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
				byte[] hashBytes = md5.ComputeHash(inputBytes);

				// Convert the byte array to hexadecimal string
				StringBuilder sb = new StringBuilder();
				for (int i = 0; i < hashBytes.Length; i++)
				{
					sb.Append(hashBytes[i].ToString("X2"));
				}
				return sb.ToString();
			}
		}
	}
}
