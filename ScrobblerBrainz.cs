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
using TagLib;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        // ListenBrainz user token.
        public string userToken;
        public System.Windows.Forms.TextBox userTokenTextBox;

        // Custom API URL.
        public string customApiUrl = "https://api.listenbrainz.org/1/submit-listens";
        public System.Windows.Forms.TextBox customApiUrlTextBox;
        
        // Settings:
        public string settingsSubfolder = "ScrobblerBrainz\\"; // Plugin settings subfolder.
        public string settingsFile = "usertoken"; // Old plugin settings file.

        // Scrobble metadata:
        DateTimeOffset timestamp;
        public string artist = "";
        public string track = "";
        public string release = "";
        public int duration_ms = 0;

        string previousPlaycount;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;

            // Plugin version:
            about.VersionMajor = 2;
            about.VersionMinor = 0;
            about.Revision = 1;

            about.Name = "ScrobblerBrainz";
            about.Description = "A scrobbler for ListenBrainz service, version " + about.VersionMajor + "." + about.VersionMinor + "." + about.Revision;
            about.Author = "karaluh";
            about.TargetApplication = "";   //  the name of a Plugin Storage device or panel header for a dockable panel
            about.Type = PluginType.General;

            about.MinInterfaceVersion = 30;
            about.MinApiRevision = 40;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 60;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            // Migrate the old config to XML if it exists
            if (System.IO.File.Exists(String.Concat(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile)))
            {
                // Get the user token from the file and save it in the XML.
                Properties.Settings.Default.userToken = System.IO.File.ReadAllText(String.Concat(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile));
                Properties.Settings.Default.Save();

                // Remove the old file.
                System.IO.File.Delete(String.Concat(mbApiInterface.Setting_GetPersistentStoragePath(), settingsSubfolder, settingsFile));
            }

            // Read the user token and custom API URL from settings.
            userToken = Properties.Settings.Default.userToken;
            customApiUrl = Properties.Settings.Default.customApiUrl;

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
                Label userTokenLabel = new Label();
                userTokenLabel.AutoSize = true;
                userTokenLabel.Location = new Point(0, 4);
                userTokenLabel.Text = "ListenBrainz user token:";
                userTokenTextBox = new System.Windows.Forms.TextBox();
                userTokenTextBox.Location = new Point(userTokenLabel.Width + 35, 0);
                // userTokenTextBox.MaxLength = 36;
                userTokenTextBox.Width = 300;
                userTokenTextBox.BackColor = Color.FromArgb(mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl,
                                                                                                        ElementState.ElementStateDefault,
                                                                                                        ElementComponent.ComponentBackground));
                userTokenTextBox.ForeColor = Color.FromArgb(mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl,
                                                                                                        ElementState.ElementStateDefault,
                                                                                                        ElementComponent.ComponentForeground));
                userTokenTextBox.BorderStyle = BorderStyle.FixedSingle;
                userTokenTextBox.Text = userToken;
                
                // Custom API URL Label and TextBox
                Label customApiUrlLabel = new Label();
                customApiUrlLabel.AutoSize = true;
                customApiUrlLabel.Location = new Point(0, 40);
                customApiUrlLabel.Text = "Custom API URL:";
                customApiUrlTextBox = new System.Windows.Forms.TextBox();
                customApiUrlTextBox.Location = new Point(customApiUrlLabel.Width + 35, 40);
                customApiUrlTextBox.Width = 300;
                customApiUrlTextBox.BackColor = Color.FromArgb(mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl,
                                                                                                           ElementState.ElementStateDefault,
                                                                                                           ElementComponent.ComponentBackground));
                customApiUrlTextBox.ForeColor = Color.FromArgb(mbApiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl,
                                                                                                           ElementState.ElementStateDefault,
                                                                                                           ElementComponent.ComponentForeground));
                customApiUrlTextBox.BorderStyle = BorderStyle.FixedSingle;
                customApiUrlTextBox.Text = Properties.Settings.Default.customApiUrl;

                configPanel.Controls.AddRange(new Control[] { userTokenLabel, userTokenTextBox, customApiUrlLabel, customApiUrlTextBox });
            }
            return false;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            // Update the user token in case it was changed by the user.
            userToken = userTokenTextBox.Text;

            // Save the user token in the XML.
            Properties.Settings.Default.userToken = userToken;
            
            // Save custom API URL
            Properties.Settings.Default.customApiUrl = customApiUrlTextBox.Text;
            
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

            void SubmitScrobble(string json) // Shared function for submitting JSON scrobbles.
            {
                for (int i = 0; i < 5; i++) // In case of temporary errors do up to 5 retries.
                {
                    try
                    {
                        submitListenResponse = httpClient.PostAsync(Properties.Settings.Default.customApiUrl, new StringContent(json, Encoding.UTF8, "application/json"));
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
                            System.IO.File.AppendAllText(String.Concat(dataPath, settingsSubfolder, "error.log"), errorTimestamp + " "
                                                                                                        + json + Environment.NewLine);
                            System.IO.File.AppendAllText(String.Concat(dataPath, settingsSubfolder, "error.log"), errorTimestamp + " "
                                                                                                        + submitListenResponse.Result.Content.ReadAsStringAsync().Result + Environment.NewLine);

                            if (json.Contains("\"listen_type\": \"single\"")) // Only execute if the listen type is single, no need to save playing_now attempts.
                            {
                                // In case there's a problem with the scrobble JSON, the error is permanent so do not retry.
                                if (submitListenResponse.Result.StatusCode.ToString() == "BadRequest")
                                {
                                    // Save the scrobble to a file and exit the loop.
                                    SaveScrobble(timestamp.ToUnixTimeSeconds().ToString(), json);
                                    break;
                                }

                                // If this is the last retry save the scrobble.
                                if (i == 4)
                                {
                                    SaveScrobble(timestamp.ToUnixTimeSeconds().ToString(), json);
                                }
                            }
                        }
                    }
                    catch // When offline, save the scrobble for a later resubmission and exit the loop.
                    {
                        if (json.Contains("\"listen_type\": \"single\"")) // Same as above.
                        {
                            SaveScrobble(timestamp.ToUnixTimeSeconds().ToString(), json);
                        }
                        break;
                    }
                }
            }

            string GenerateMbidJson()
            {
                string mbidJson = "";
                var file = TagLib.File.Create(mbApiInterface.NowPlaying_GetFileUrl());
                string[] trackArtistMbids = new string[0];

                // Implementation of artist tags is dependent on how MBID tags are saved by MusicBrainz Picard and how TagLib# 2.2.0.0 exposes these tags.
                // While one can technically, for example, have ID3 or APE inside of an AAC, this isn't how Picard saves MBIDs in tags, and it's not a good idea to implement anything out of MusicBrainz's standards.
                // TODO: simplify all this code once a new version of TagLib# is released.

                if (file.GetTag(TagTypes.Xiph) != null)
                { // Xiph / Vorbis comments
                    // TagLib# 2.2.0.0 does not support grabbing multiple artist MBIDs on Vorbis, but they're still retrievable directly.
                    // This merged PR will allow this but TagLib# itself has not updated yet: https://github.com/mono/taglib-sharp/pull/253
                    // this will be simplified once either TagLib# updates or MBIDs become retrievable in the MusicBee API.
                    var fileTags = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
                    trackArtistMbids = fileTags.GetField("MUSICBRAINZ_ARTISTID");
                }
                else
                {
                    // TagLib# 2.2.0.0 seems to have inconsistent ways of presenting multiple artist MBIDs.
                    // ID3v2.4 seems to be split into semicolons by TagLib#, APE uses commas
                    // Other codecs seem to only support one (e.g. MP4, ASF).
                    // MKA is not officially supported by MusicBrainz Picard.
                    if (file.Tag.MusicBrainzArtistId != null)
                    {
                        trackArtistMbids = file.Tag.MusicBrainzArtistId.Split(new Char[] { ';', '/', ',' });
                    }
                }

                // Make the artist MBID JSON
                if (trackArtistMbids.Length != 0)
                {
                    string trackArtistMbidJson = "\"artist_mbids\": [";
                    foreach (string mbid in trackArtistMbids)
                    {
                        trackArtistMbidJson += "\"" + mbid.Trim() + "\",";
                    }
                    trackArtistMbidJson = trackArtistMbidJson.TrimEnd(',');
                    trackArtistMbidJson += "], ";
                    mbidJson += trackArtistMbidJson;
                }

                mbidJson += (!String.IsNullOrEmpty(file.Tag.MusicBrainzTrackId)) ? String.Format("\"recording_mbid\": \"{0}\",", file.Tag.MusicBrainzTrackId) : "";
                mbidJson += (!String.IsNullOrEmpty(file.Tag.MusicBrainzReleaseId)) ? String.Format("\"release_mbid\": \"{0}\",", file.Tag.MusicBrainzReleaseId) : "";
                mbidJson += (!String.IsNullOrEmpty(file.Tag.MusicBrainzReleaseGroupId)) ? String.Format("\"release_group_mbid\": \"{0}\",", file.Tag.MusicBrainzReleaseGroupId) : "";
                mbidJson += (file.Tag.Track != 0) ? String.Format("\"trackNumber\": \"{0}\",", file.Tag.Track) : "";
                return mbidJson;
            }

            // Perform some action depending on the notification type.
            switch (type)
            {
                case NotificationType.PluginStartup: // Perform startup initialisation.
                    // Get the timestamp in epoch.
                    // Offset by current playing position assuming the user didn't pause prior to the extension loading,
                    // should be accurate enough 99% of the time.
                    timestamp = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(mbApiInterface.Player_GetPosition());

                    // Get the metadata of the track selected by MusicBee on startup to know what to scrobble.
                    artist = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    track = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    release = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));

                    // Get the current playcount to see if it changes or if the song was skipped.
                    previousPlaycount = mbApiInterface.NowPlaying_GetFileProperty(FilePropertyType.PlayCount);

                    // Re-scrobble any offline scrobbles.
                    try
                    {
                        string[] offlineScrobbles = Directory.GetFiles(String.Concat(dataPath, settingsSubfolder, "scrobbles"));
                        for (int i = 0; i < offlineScrobbles.Length; i++)
                        {
                            if (!String.IsNullOrEmpty(userToken)) // But only if the user token is configured.
                            {
                                try
                                {
                                    submitListenResponse = httpClient.PostAsync(Properties.Settings.Default.customApiUrl, new StringContent(System.IO.File.ReadAllText(offlineScrobbles[i]), Encoding.UTF8, "application/json"));
                                    if (submitListenResponse.Result.IsSuccessStatusCode) // If the re-scrobble succeedes, remove the file.
                                    {
                                        try
                                        {
                                            System.IO.File.Delete(offlineScrobbles[i]);
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
                    }
                    catch (DirectoryNotFoundException) // Handle the "no offline scroble directory" exception.
                    {
                        // Do nothing, there's just nothing to re-scrobble.
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
                    timestamp = DateTimeOffset.UtcNow; // Get the timestamp in epoch.
                    artist = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    track = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle));
                    release = HttpUtility.JavaScriptStringEncode(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album));
                    duration_ms = mbApiInterface.NowPlaying_GetDuration();

                    // Get the current playcount to see if it changes or if the song was skipped.
                    previousPlaycount = mbApiInterface.NowPlaying_GetFileProperty(FilePropertyType.PlayCount);

                    // Prepare the scrobble.
                    if (!String.IsNullOrEmpty(userToken))
                    {
                        string nowPlayingJson = "{\"listen_type\": \"playing_now\", \"payload\": [ { \"track_metadata\": {\"artist_name\": \""
                                                  + artist + "\", \"track_name\": \"" + track + "\", \"release_name\": \"" + release
                                                  + "\", \"additional_info\": {" + GenerateMbidJson() + "\"duration_ms\":" + duration_ms + "," +
                                                  "\"media_player\": \"MusicBee\", \"submission_client\": \"ScrobblerBrainz\"} } } ] }"; // Set the scrobble JSON.
                        SubmitScrobble(nowPlayingJson);
                    }

                    break;

                case NotificationType.PlayCountersChanged: // This is emitted each time either a play count OR a skip count increases.
                    // Scrobble the track but only if the user token is configured and the song wasn't skipped.
                    if (!String.IsNullOrEmpty(userToken) && !(previousPlaycount == mbApiInterface.Library_GetFileProperty(sourceFileUrl, FilePropertyType.PlayCount)))
                    {
                        // Prepare the scrobble.
                        string submitListenJson = "{\"listen_type\": \"single\", \"payload\": [ { \"listened_at\": "
                                                  + timestamp.ToUnixTimeSeconds() + ",\"track_metadata\": {\"artist_name\": \""
                                                  + artist + "\", \"track_name\": \"" + track + "\", \"release_name\": \"" + release
                                                  + "\", \"additional_info\": {" + GenerateMbidJson() + "\"duration_ms\":" + duration_ms + "," +
                                                  "\"media_player\": \"MusicBee\", \"submission_client\": \"ScrobblerBrainz\"} } } ] }"; // Set the scrobble JSON.

                        SubmitScrobble(submitListenJson);
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
            System.IO.File.WriteAllText(String.Concat(dataPath, settingsSubfolder, "scrobbles\\", timestamp, ".json"), json);
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
