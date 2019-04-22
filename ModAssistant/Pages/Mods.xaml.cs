﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Compression;
using System.Diagnostics;
using System.Windows.Forms;

namespace ModAssistant.Pages
{
    /// <summary>
    /// Interaction logic for Mods.xaml
    /// </summary>
    public sealed partial class Mods : Page
    {
        public static Mods Instance = new Mods();

        public string[] DefaultMods = { "SongLoader", "ScoreSaber", "BeatSaverDownloader" };
        public Mod[] ModsList;
        public static List<Mod> InstalledMods = new List<Mod>();
        public List<string> CategoryNames = new List<string>();
        public CollectionView view;

        public List<ModListItem> ModList { get; set; }

        public Mods()
        {
            InitializeComponent();

            ModList = new List<ModListItem>();

            LoadMods();
        }

        private void RefreshModsList()
        {
            view.Refresh();
        }

        private async void LoadMods()
        {
            if (App.CheckInstalledMods)
            {
                MainWindow.Instance.MainText = "Checking Installed Mods...";
                await Task.Run(() => CheckInstalledMods());
            } else
            {
                InstalledColumn.Width = 0;
                UninstallColumn.Width = 0;
                DescriptionColumn.Width = 800;
            }

            MainWindow.Instance.MainText = "Loading Mods...";
            await Task.Run(() => PopulateModsList());

            ModsListView.ItemsSource = ModList;

            view = (CollectionView)CollectionViewSource.GetDefaultView(ModsListView.ItemsSource);
            PropertyGroupDescription groupDescription = new PropertyGroupDescription("Category");
            view.GroupDescriptions.Add(groupDescription);

            this.DataContext = this;

            RefreshModsList();
            MainWindow.Instance.MainText = "Finished Loading Mods.";
            MainWindow.Instance.InstallButton.IsEnabled = true;
        }

        private void CheckInstalledMods()
        {
            List<string> empty = new List<string>();
            CheckInstallDir("Plugins", empty);
            CheckInstallDir(@"IPA\Libs", empty);
        }

        private void CheckInstallDir(string directory, List<string> blacklist)
        {
            foreach (string file in Directory.GetFileSystemEntries(System.IO.Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                if (File.Exists(file) && System.IO.Path.GetExtension(file) == ".dll")
                {
                    Mod mod = GetModFromHash(Utils.CalculateMD5(file));
                    if (mod != null)
                    {
                        if (!InstalledMods.Contains(mod))
                        {
                            InstalledMods.Add(mod);
                        }
                    }
                }
            }
        }

        private Mod GetModFromHash(string hash)
        {
            string json = string.Empty;
            Mod[] modMatches;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Utils.Constants.BeatModsAPIUrl + "mod?hash=" + hash);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                var serializer = new JavaScriptSerializer();
                modMatches = serializer.Deserialize<Mod[]>(reader.ReadToEnd());
            }

            if (modMatches.Length == 0)
                return null;

            return modMatches[0];
        }

        public void PopulateModsList()
        {
            string json = string.Empty;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Utils.Constants.BeatModsAPIUrl + Utils.Constants.BeatModsModsOptions);
            request.AutomaticDecompression = DecompressionMethods.GZip;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                var serializer = new JavaScriptSerializer();
                ModsList = serializer.Deserialize<Mod[]>(reader.ReadToEnd());
            }

            foreach (Mod mod in ModsList)
            {
                bool preSelected = mod.required;
                if (DefaultMods.Contains(mod.name) || (App.SaveModSelection && App.SavedMods.Contains(mod.name)))
                {
                    preSelected = true;
                    if(!App.SavedMods.Contains(mod.name))
                    {
                        App.SavedMods.Add(mod.name);
                    }
                }

                RegisterDependencies(mod);

                ModListItem ListItem = new ModListItem()
                {
                    IsSelected = preSelected,
                    IsEnabled = !mod.required,
                    ModName = mod.name,
                    ModVersion = mod.version,
                    ModDescription = mod.description.Replace('\n', ' '),
                    ModInfo = mod,
                    Category = mod.category
                };

                foreach (Mod installedMod in InstalledMods)
                {
                    if (mod.name == installedMod.name)
                    {
                        ListItem.IsInstalled = true;
                        ListItem.InstalledVersion = installedMod.version;
                        break;
                    }
                }

                mod.ListItem = ListItem;

                ModList.Add(ListItem);
            }

            foreach (Mod mod in ModsList)
            {
                ResolveDependencies(mod);
            }
        }

        public async void InstallMods ()
        {
            MainWindow.Instance.InstallButton.IsEnabled = false;
            string installDirectory = App.BeatSaberInstallDirectory;

            foreach (Mod mod in ModsList)
            {
                if (mod.name.ToLower() == "bsipa")
                {
                    MainWindow.Instance.MainText = $"Installing {mod.name}...";
                    await Task.Run(() => InstallMod(mod, installDirectory));
                    MainWindow.Instance.MainText = $"Installed {mod.name}.";
                    await Task.Run(() =>
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = System.IO.Path.Combine(installDirectory, "IPA.exe"),
                            WorkingDirectory = installDirectory,
                            Arguments = "-n"
                        }).WaitForExit()
                    );
                }
                else if(mod.ListItem.IsSelected)
                {
                    MainWindow.Instance.MainText = $"Installing {mod.name}...";
                    await Task.Run(() => InstallMod(mod, System.IO.Path.Combine(installDirectory, @"IPA\Pending")));
                    MainWindow.Instance.MainText = $"Installed {mod.name}.";
                }
            }
            MainWindow.Instance.MainText = "Finished installing mods.";
            MainWindow.Instance.InstallButton.IsEnabled = true;
        }

        private void InstallMod (Mod mod, string directory)
        {
            string downloadLink = null;

            foreach (Mod.DownloadLink link in mod.downloads)
            {
                if (link.type == "universal")
                {
                    downloadLink = link.url;
                    break;
                } else if (link.type.ToLower() == App.BeatSaberInstallType.ToLower())
                {
                    downloadLink = link.url;
                    break;
                }
            }

            if (String.IsNullOrEmpty(downloadLink))
            {
                System.Windows.MessageBox.Show($"Could not find download link for {mod.name}");
                return;
            }

            using (MemoryStream stream = new MemoryStream(DownloadMod(Utils.Constants.BeatModsURL + mod.downloads[0].url)))
            {
                using (ZipArchive archive = new ZipArchive(stream))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        string fileDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.Combine(directory, file.FullName));
                        if (!Directory.Exists(fileDirectory))
                            Directory.CreateDirectory(fileDirectory);

                        if(!String.IsNullOrEmpty(file.Name))
                            file.ExtractToFile(System.IO.Path.Combine(directory, file.FullName), true);
                    }
                }
            }
        }

        private byte[] DownloadMod (string link)
        {
            byte[] zip = new WebClient().DownloadData(link);
            return zip;
        }

        private void RegisterDependencies(Mod dependent)
        {
            if (dependent.dependencies.Length == 0)
                return;

            foreach (Mod mod in ModsList)
            {
                foreach (Mod.Dependency dep in dependent.dependencies)
                {
                    
                    if (dep.name == mod.name)
                    {
                        dep.Mod = mod;
                        mod.Dependents.Add(dependent);
                        
                    }
                }
            }
        }

        private void ResolveDependencies(Mod dependent)
        {
            if (dependent.ListItem.IsSelected && dependent.dependencies.Length > 0)
            {
                foreach (Mod.Dependency dependency in dependent.dependencies)
                {
                    if (dependency.Mod.ListItem.IsEnabled)
                    {
                        dependency.Mod.ListItem.PreviousState = dependency.Mod.ListItem.IsSelected;
                        dependency.Mod.ListItem.IsSelected = true;
                        dependency.Mod.ListItem.IsEnabled = false;
                        ResolveDependencies(dependency.Mod);
                    }
                }
            }
        }

        private void UnresolveDependencies(Mod dependent)
        {
            if (!dependent.ListItem.IsSelected && dependent.dependencies.Length > 0)
            {
                foreach (Mod.Dependency dependency in dependent.dependencies)
                {
                    if (!dependency.Mod.ListItem.IsEnabled)
                    {
                        bool needed = false;
                        foreach (Mod dep in dependency.Mod.Dependents)
                        {
                            if (dep.ListItem.IsSelected)
                            {
                                needed = true;
                                break;
                            }
                        }
                        if (!needed && !dependency.Mod.required)
                        {
                            dependency.Mod.ListItem.IsSelected = dependency.Mod.ListItem.PreviousState;
                            dependency.Mod.ListItem.IsEnabled = true;
                            UnresolveDependencies(dependency.Mod);
                        }
                    }
                }
            }
        }

        private void ModCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.CheckBox).Tag as Mod);
            mod.ListItem.IsSelected = true;
            ResolveDependencies(mod);
            App.SavedMods.Add(mod.name);
            Properties.Settings.Default.SavedMods = String.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();
            RefreshModsList();
        }

        private void ModCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.CheckBox).Tag as Mod);
            mod.ListItem.IsSelected = false;
            UnresolveDependencies(mod);
            App.SavedMods.Remove(mod.name);
            Properties.Settings.Default.SavedMods = String.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();
            RefreshModsList();
        }

        public class Category
        {
            public string CategoryName { get; set; }
            public List<ModListItem> Mods = new List<ModListItem>();
        }

        public class ModListItem
        {
            public string ModName { get; set; }
            public string ModVersion { get; set; }
            public string ModDescription { get; set; }
            public bool PreviousState { get; set; }

            public bool IsEnabled { get; set; }
            public bool IsSelected { get; set; }
            public Mod ModInfo { get; set; }
            public string Category { get; set; }

            public bool IsInstalled { get; set; }
            private string _installedVersion { get; set; }
            public string InstalledVersion {
                get
                {
                    if (String.IsNullOrEmpty(_installedVersion) || !IsInstalled)
                    {
                        return "-";
                    }
                    else
                    {
                        return _installedVersion;
                    }
                }
                set
                {
                    _installedVersion = value;
                }
            }
            public string IsOutdated {
                get
                {
                    if (IsInstalled)
                    {
                        if (InstalledVersion == ModVersion)
                        {
                            return "Green"; //green
                        } else
                        {
                            return "Red"; //red
                        }
                    }
                    else
                    {
                        return "Black"; //grey
                    }
                }
            }

            public bool CanDelete
            {
                get
                {
                    return (!ModInfo.required && IsInstalled);
                }
            }

            public string CanSeeDelete
            {
                get
                {
                    if (!ModInfo.required && IsInstalled)
                        return "Visible";
                    else
                        return "Hidden";
                }
            }

        }

        private void ModsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MainWindow.Instance.InfoButton.IsEnabled = true;
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.Button).Tag as Mod);
            if (System.Windows.Forms.MessageBox.Show($"Uninstall {mod.name}?", $"Are you sure you want to remove {mod.name}?\nThis can break other of your mods.", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                Mod.DownloadLink links = null;
                foreach (Mod.DownloadLink link in mod.downloads)
                {
                    if (link.type.ToLower() == "universal" || link.type.ToLower() == App.BeatSaberInstallType.ToLower())
                    {
                        links = link;
                        break;
                    }
                }
                foreach (Mod.FileHashes files in links.hashMd5)
                {
                    File.Delete(System.IO.Path.Combine(App.BeatSaberInstallDirectory, files.file));
                }
                mod.ListItem.IsInstalled = false;
                mod.ListItem.InstalledVersion = null;
            }
        }
    }
}