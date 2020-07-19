using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;


namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        // ListenBrainz user token.
        public string userToken;
        public TextBox userTokenTextBox;

        // Play count sync setting.
        public bool playCountSync;
        public CheckBox playCountSyncCheckBox;

        // Settings:
        public string settingsSubfolder = "ScrobblerBrainz\\"; // Plugin settings subfolder.
        public string settingsFile = "usertoken"; // Old plugin settings file.

        // Scrobble metadata:
        TimeSpan timestamp;
        public string artist = "";
        public string track = "";
        public string release = "";

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "ScrobblerBrainz";
            about.Description = "A scrobbler for ListenBrainz service";
            about.Author = "karaluh";
            about.TargetApplication = "";   //  the name of a Plugin Storage device or panel header for a dockable panel
            about.Type = PluginType.General;

            // Plugin version:
            about.VersionMajor = 0;
            about.VersionMinor = 1;
            about.Revision = 0;

            about.MinInterfaceVersion = 30;
            about.MinApiRevision = 40;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 50;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            // Migrate the old config to XML if it exists
            if(File.Exists(String.Concat(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile)))
            {
                // Get the user token from the file and save it in the XML.
                Properties.Settings.Default.userToken = File.ReadAllText(String.Concat(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile));
                Properties.Settings.Default.Save();

                // Remove the old file.
                File.Delete(String.Concat(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile));
            }

            // Read the settings from a file.
            userToken = Properties.Settings.Default.userToken;
            playCountSync = Properties.Settings.Default.playCountSync;

            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            // panelHandle will only be set if you set about.ConfigurationPanelHeight to a non-zero value
            // keep in mind the panel width is scaled according to the font the user has selected
            // if about.ConfigurationPanelHeight is set to 0, you can display your own popup window
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                configPanel.AutoSize = true;
                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(0, 0);
                prompt.Text = "ListenBrainz User token:";
                userTokenTextBox = new TextBox();
                //userTokenTextBox.Size = new Size(104, 16);
                userTokenTextBox.Bounds = new Rectangle(135, 0, 100, userTokenTextBox.Height);
                userTokenTextBox.Text = userToken;

                // Play count sync related controls.
                // Label.
                Label playCountSyncLabel = new Label();
                playCountSyncLabel.Location = new Point(0, 25);
                playCountSyncLabel.AutoSize = true;
                playCountSyncLabel.Text = "synchronise from ListenBrainz to your library on startup:";
                // Check box.
                playCountSyncCheckBox = new CheckBox();
                playCountSyncCheckBox.Location = new Point(0, 43);
                playCountSyncCheckBox.AutoSize = true;
                playCountSyncCheckBox.Text = "synchronise Play Count";
                playCountSyncCheckBox.Checked = playCountSync;
                
                // Add all of the controls to the panel.
                configPanel.Controls.AddRange(new Control[] { prompt, userTokenTextBox, playCountSyncLabel, playCountSyncCheckBox });
            }
            return false;
        }
       
        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            // Update the settings in case they were changed by the user.
            userToken = userTokenTextBox.Text;
            playCountSync = playCountSyncCheckBox.Checked;

            // Save the user token in the XML.
            Properties.Settings.Default.userToken = userToken;
            Properties.Settings.Default.playCountSync = playCountSync;
            Properties.Settings.Default.Save();
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
            // Nothing is being done here because the settings are left after uninstalation by design.
        }

        // Receive event notifications from MusicBee.
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // Get the MusicBee settings path for later.
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();

            // Prepare the HTTP client instance for later.
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", userToken); // Set the authorization headers.
            System.Threading.Tasks.Task<HttpResponseMessage> submitListenResponse;

            // Perform some action depending on the notification type.
            switch (type)
            {
                case NotificationType.PluginStartup: // Perform startup initialisation.
                    // Get the metadata of the track selected by MusicBee on startup to know what to scrobble.
                    artist = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    track = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    release = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));

                    // Re-scrobble any offline scrobbles.
                    string[] offlineScrobbles = Directory.GetFiles(String.Concat(dataPath, settingsSubfolder, "scrobbles"));
                    for (int i = 0; i < offlineScrobbles.Length; i++)
                    {
                        if (!String.IsNullOrEmpty(userToken)) // But only if the user token is configured.
                        {
                            try
                            {
                                submitListenResponse = httpClient.PostAsync("https://api.listenbrainz.org/1/submit-listens", new StringContent(File.ReadAllText(offlineScrobbles[i]), Encoding.UTF8, "application/json"));
                                if (submitListenResponse.Result.IsSuccessStatusCode) // If the scrobble succeedes, remove the file.
                                {
                                    try
                                    {
                                        File.Delete(offlineScrobbles[i]);
                                    }
                                    catch (IOException) // Handle the case where the saved scrobble is opened.
                                    {
                                        // Do nothing, the file will be removed on the next run.
                                    }
                                }
                            }
                            catch // Handle the connectivity issues exception.
                            {
                                // Do nothing, the file will be re-scrobbled on the next run.
                            }
                        }
                    }
                    //switch (mbApiInterface.Player_GetPlayState())
                    //{
                    //    case PlayState.Playing:
                    //    case PlayState.Paused:
                    //        artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                    //        break;
                    //}
                    break;

                case NotificationType.TrackChanged: // Update the metadata on track change.
                    artist = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    track = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    release = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));
                    break;

                case NotificationType.PlayCountersChanged: // Scrobble the track when playcount is changed.
                    if (!String.IsNullOrEmpty(userToken)) // But only if the user token is configured.
                    {
                        timestamp = DateTime.UtcNow - new DateTime(1970, 1, 1); // Get the timestamp in epoch.

                        // Prepare the scrobble.
                        string submitListenJson = "{\"listen_type\": \"single\", \"payload\": [ { \"listened_at\": "
                                                  + (int)timestamp.TotalSeconds + ",\"track_metadata\": {\"artist_name\": \""
                                                  + artist + "\", \"track_name\": \"" + track + "\", \"release_name\": \"" + release
                                                  + "\", \"additional_info\": {\"listening_from\": \"MusicBee\"} } } ] }"; // Set the scrobble JSON.

                        // Post the scrobble.
                        for (int i = 0; i < 5; i++) // In case of temporary errors do up to 5 retries.
                        {
                            try
                            {
                                submitListenResponse = httpClient.PostAsync("https://api.listenbrainz.org/1/submit-listens", new StringContent(submitListenJson, Encoding.UTF8, "application/json"));
                                if (submitListenResponse.Result.IsSuccessStatusCode) // If the scrobble succeedes, exit the loop.
                                {
                                     break;
                                }
                                else // If the scrobble fails save it for a later resubmission and log the error.
                                {
                                    // Log the timestamp, the failed scrobble and the error message in the error file.
                                    string errorTimestamp = DateTime.Now.ToString();
                                    
                                    // Create the folder where the error log will be stored.
                                    Directory.CreateDirectory(String.Concat(dataPath, settingsSubfolder));
                                    File.AppendAllText(String.Concat(dataPath, settingsSubfolder, "error.log"), errorTimestamp + " "
                                                                                                                + submitListenJson + Environment.NewLine);
                                    File.AppendAllText(String.Concat(dataPath, settingsSubfolder, "error.log"), errorTimestamp + " "
                                                                                                                + submitListenResponse.Result.Content.ReadAsStringAsync().Result + Environment.NewLine);

                                    // In case there's a problem with the scrobble JSON, the error is permanent so do not retry.
                                    if (submitListenResponse.Result.StatusCode.ToString() == "BadRequest")
                                    {
                                        // Save the scrobble to a file and exit the loop.
                                        SaveScrobble(timestamp.TotalSeconds.ToString(), submitListenJson);
                                        break;
                                    }

                                    // If this is the last retry save the scrobble.
                                    if (i == 4)
                                    {
                                        SaveScrobble(timestamp.TotalSeconds.ToString(), submitListenJson);
                                    }
                                }
                            }
                            catch // When offline, save the scrobble for a later resubmission and exit the loop.
                            {
                                SaveScrobble(timestamp.TotalSeconds.ToString(), submitListenJson);
                                break;
                            }
                        }
                    }
                    break;
            }
        }

        public void SaveScrobble(string timestamp, string json)
        {
            // Create the folder where offline scrobbles will be stored.
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            Directory.CreateDirectory(String.Concat(dataPath, settingsSubfolder, "scrobbles"));

            // Save the scrobble.
            File.WriteAllText(String.Concat(dataPath, settingsSubfolder, "scrobbles\\", timestamp, ".json"), json);
        }
              
        // return an array of lyric or artwork provider names this plugin supports
        // the providers will be iterated through one by one and passed to the RetrieveLyrics/ RetrieveArtwork function in order set by the user in the MusicBee Tags(2) preferences screen until a match is found
        //public string[] GetProviders()
        //{
        //    return null;
        //}

        // return lyrics for the requested artist/title from the requested provider
        // only required if PluginType = LyricsRetrieval
        // return null if no lyrics are found
        //public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred, string provider)
        //{
        //    return null;
        //}

        // return Base64 string representation of the artwork binary data from the requested provider
        // only required if PluginType = ArtworkRetrieval
        // return null if no artwork is found
        //public string RetrieveArtwork(string sourceFileUrl, string albumArtist, string album, string provider)
        //{
        //    //Return Convert.ToBase64String(artworkBinaryData)
        //    return null;
        //}

        //  presence of this function indicates to MusicBee that this plugin has a dockable panel. MusicBee will create the control and pass it as the panel parameter
        //  you can add your own controls to the panel if needed
        //  you can control the scrollable area of the panel using the mbApiInterface.MB_SetPanelScrollableArea function
        //  to set a MusicBee header for the panel, set about.TargetApplication in the Initialise function above to the panel header text
        //public int OnDockablePanelCreated(Control panel)
        //{
        //  //    return the height of the panel and perform any initialisation here
        //  //    MusicBee will call panel.Dispose() when the user removes this panel from the layout configuration
        //  //    < 0 indicates to MusicBee this control is resizable and should be sized to fill the panel it is docked to in MusicBee
        //  //    = 0 indicates to MusicBee this control resizeable
        //  //    > 0 indicates to MusicBee the fixed height for the control.Note it is recommended you scale the height for high DPI screens(create a graphics object and get the DpiY value)
        //    float dpiScaling = 0;
        //    using (Graphics g = panel.CreateGraphics())
        //    {
        //        dpiScaling = g.DpiY / 96f;
        //    }
        //    panel.Paint += panel_Paint;
        //    return Convert.ToInt32(100 * dpiScaling);
        //}

        // presence of this function indicates to MusicBee that the dockable panel created above will show menu items when the panel header is clicked
        // return the list of ToolStripMenuItems that will be displayed
        //public List<ToolStripItem> GetHeaderMenuItems()
        //{
        //    List<ToolStripItem> list = new List<ToolStripItem>();
        //    list.Add(new ToolStripMenuItem("A menu item"));
        //    return list;
        //}

        //private void panel_Paint(object sender, PaintEventArgs e)
        //{
        //    e.Graphics.Clear(Color.Red);
        //    TextRenderer.DrawText(e.Graphics, "hello", SystemFonts.CaptionFont, new Point(10, 10), Color.Blue);
        //}

    }
}