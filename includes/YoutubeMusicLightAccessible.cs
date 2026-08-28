using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace YoutubeMusicLightAccessible
{
    public class Track
    {
        public string Kind = "track";
        public string Title = "";
        public string Channel = "";
        public string Duration = "";
        public string Url = "";
        public string VideoId = "";
        public string BrowseId = "";
        public string PlaylistId = "";
        public string LikeStatus = "";
        public string Published = "";

        public override string ToString()
        {
            string type = Kind == "playlist" ? "Playlist: " : Kind == "channel" ? "Canal: " : "";
            string channel = String.IsNullOrWhiteSpace(Channel) ? "canal desconhecido" : Channel;
            string duration = String.IsNullOrWhiteSpace(Duration) ? "duração desconhecida" : Duration;
            return type + Title + ", " + channel + ", " + duration;
        }
    }

    public class AudioDevice
    {
        public string Id = "";
        public string Name = "";

        public override string ToString()
        {
            return String.IsNullOrWhiteSpace(Name) ? Id : Name;
        }
    }

    public class GithubReleaseUpdate
    {
        public string Version = "";
        public string ZipUrl = "";
        public string ShaUrl = "";
        public string Notes = "";
    }

    public class ChapterPoint
    {
        public string Title = "";
        public double StartSeconds = 0;

        public override string ToString()
        {
            return FormatSecondsForDisplay(StartSeconds) + ", " + (String.IsNullOrWhiteSpace(Title) ? "Capítulo sem título" : Title);
        }

        private static string FormatSecondsForDisplay(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalHours >= 1) return ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
            return span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
        }
    }

    public class MainForm : Form, IMessageFilter
    {
        private readonly string baseDir;
        private readonly string configDir;
        private readonly string localDataDir;
        private readonly string legacyConfigDir;
        private readonly string libraryDir;
        private readonly string legacyLibraryDir;
        private readonly string runtimeDir;
        private readonly string defaultDownloadDir;
        private readonly string tempAudioDir;
        private readonly string configFile;
        private readonly string pendingUpdateNotesFile;
        private readonly string notifiedVideosFile;
        private readonly string dependenciesOkFile;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly List<Track> tracks = new List<Track>();
        private readonly List<Track> playbackQueue = new List<Track>();
        private readonly List<Track> localFavorites = new List<Track>();
        private readonly List<Track> localHistory = new List<Track>();
        private readonly Dictionary<string, Keys> customShortcuts = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> notifiedVideoKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private TextBox searchBox;
        private TableLayoutPanel searchPanel;
        private FlowLayoutPanel topActionsPanel;
        private GroupBox feedGroup;
        private TableLayoutPanel resultsPanel;
        private GroupBox playerGroup;
        private GroupBox moreGroup;
        private Label mainMessageLabel;
        private ListBox feedList;
        private ListBox moreList;
        private ListBox resultsList;
        private Label statusLabel;
        private ProgressBar progressBar;
        private ListBox playerList;
        private NotifyIcon trayIcon;

        private object internalPlayer;
        private Process vlcProcess;
        private StreamWriter vlcInput;
        private readonly object vlcLock = new object();
        private bool usingVlc = false;
        private Process playerMonitorProcess;
        private StreamWriter playerMonitorInput;
        private readonly object playerMonitorLock = new object();
        private string currentMediaPath = "";
        private Process micMonitorProcess;
        private StreamWriter micMonitorInput;
        private readonly object micMonitorLock = new object();
        private Process mpvProcess;
        private StreamWriter mpvInput;
        private readonly object mpvLock = new object();
        private bool usingMpv = false;
        private Process ffplayProcess;
        private readonly object ffplayLock = new object();
        private bool usingFfplay = false;
        private bool ytdlpRepairAttempted = false;
        private string currentTrackTitle = "";
        private string currentTrackUrl = "";
        private string currentVideoId = "";
        private string currentTrackDuration = "";
        private string currentTempAudioPath = "";
        private Track currentTrack;
        private int savedVolume = 35;
        private bool hasSavedVolume = false;
        private string selectedDownloadDir = "";
        private string selectedOutputDeviceId = "";
        private string selectedOutputDeviceName = "";
        private string selectedMonitorOutputDeviceId = "";
        private string selectedMonitorOutputDeviceName = "";
        private bool playerMonitorEnabled = false;
        private int playerMonitorVolume = 70;
        private string selectedInputDeviceName = "";
        private string selectedMicOutputDeviceId = "";
        private string selectedMicOutputDeviceName = "";
        private bool micMonitorEnabled = false;
        private bool micMuted = true;
        private int micVolume = 70;
        private string audioListenMode = "video";
        private bool announcePlayerEvents = false;
        private bool infiniteRadio = false;
        private bool normalizeVolume = true;
        private bool musicOnlyMode = false;
        private bool preferTemporaryAudio = true;
        private bool autoplayEnabled = true;
        private bool playbackPaused = false;
        private bool isExitPromptOpen = false;
        private bool localFolderAudioOnly = false;
        private bool repeatOnceConsumed = false;
        private string localFolderPlaybackMode = "normal";
        private bool realtimeVideoNotifications = false;
        private bool autoReadVideoNotifications = true;
        private bool feedSearchExpanded = true;
        private bool feedAccountExpanded = true;
        private bool feedExploreExpanded = true;
        private bool feedLibraryExpanded = true;
        private int volumeBoostPercent = 100;
        private int altShiftSeekSeconds = 10;
        private int notificationIntervalMinutes = 10;
        private int lastProgressAnnouncement = -1;
        private int currentIndex = -1;
        private bool playbackStarted = false;
        private bool suppressAutoAdvance = false;
        private DateTime playbackStartedAt = DateTime.MinValue;
        private System.Windows.Forms.Timer playbackTimer;
        private System.Windows.Forms.Timer historyCleanupTimer;
        private System.Windows.Forms.Timer notificationTimer;
        private bool altSearchExpanded = false;
        private bool altPcPlayerExpanded = false;
        private bool altConverterExpanded = false;
        private bool altPlaybackExpanded = false;
        private bool altSettingsExpanded = false;
        private bool altMoreExpanded = false;
        private readonly Dictionary<string, string> streamCache = new Dictionary<string, string>();

        private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
        private const uint EVENT_OBJECT_VALUECHANGE = 0x800E;
        private const int OBJID_CLIENT = -4;
        private const int CHILDID_SELF = 0;
        private const string AppVersion = "3.12.2";
        private const string AppUpdatedAt = "28/08/2026";
        private const string GitHubOwner = "diegovinicius95891-netizen";
        private const string GitHubRepo = "Youtube-Light";
        private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";
        private const string GitHubReleasePageUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases";
        private const string UpdateAssetPattern = @"^Youtube-Light-Portable-(?<version>\d+\.\d+\.\d+)\.zip$";
        private const string UpdateShaAssetSuffix = ".sha256";
        private const string LastUpdateCheckFileName = "ultima_verificacao_atualizacao.dat";
        private const string IgnoredUpdateFileName = "atualizacao_ignorada.dat";
        private const string StandaloneYtdlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string YoutubeDlExeUrl = "https://github.com/ytdl-org/youtube-dl/releases/latest/download/youtube-dl.exe";
        private const string PythonEmbedUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";
        private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll")]
        private static extern void NotifyWinEvent(uint eventType, IntPtr hwnd, int idObject, int idChild);

        [DllImport(@"librarys\nvdaControllerClient64.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText(string text);

        public MainForm()
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            legacyConfigDir = Path.Combine(baseDir, "config");
            configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoutubeLight");
            localDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeLight");
            libraryDir = Path.Combine(baseDir, "librarys");
            legacyLibraryDir = Path.Combine(baseDir, "Library");
            runtimeDir = Path.Combine(libraryDir, "py");
            defaultDownloadDir = Path.Combine(configDir, "downloads");
            tempAudioDir = Path.Combine(localDataDir, "cache_audio");
            configFile = Path.Combine(configDir, "player_config.dat");
            pendingUpdateNotesFile = Path.Combine(configDir, "pending_update_notes.dat");
            notifiedVideosFile = Path.Combine(configDir, "videos_notificados.dat");
            dependenciesOkFile = Path.Combine(configDir, "dependencias_ok.dat");
            selectedDownloadDir = defaultDownloadDir;
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(localDataDir);
            Directory.CreateDirectory(libraryDir);
            MigrateLegacyLayout();
            Directory.CreateDirectory(defaultDownloadDir);
            Directory.CreateDirectory(tempAudioDir);
            LoadConfig();
            LoadLocalData();
            LoadNotifiedVideos();
            ApplyDefaultShortcuts();
            Directory.CreateDirectory(GetDownloadDir());

            UpdateWindowTitle();
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(980, 680);
            MinimumSize = new Size(820, 500);
            KeyPreview = true;
            Application.AddMessageFilter(this);

            BuildUi();
            SetupTrayIcon();
            SetupPlaybackTimer();
            SetupHistoryCleanupTimer();
            SetupNotificationTimer();
            Shown += delegate
            {
                if (mainMessageLabel != null) mainMessageLabel.Focus();
                UpdateLoginButtons();
                if (micMonitorEnabled && audioListenMode != "video") StartMicMonitor();
                UpdateDependencies(true);
                AnnouncePendingUpdateNotes();
                BeginDelayedAppUpdateCheck();
            };
            Resize += MainFormResize;
            FormClosing += delegate
            {
                SaveCurrentVolume();
                SaveLocalData();
                StopPlayback();
                StopMicMonitor();
                Application.RemoveMessageFilter(this);
                if (playbackTimer != null) playbackTimer.Dispose();
                if (historyCleanupTimer != null) historyCleanupTimer.Dispose();
                if (notificationTimer != null) notificationTimer.Dispose();
                if (trayIcon != null) trayIcon.Dispose();
            };
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSKEYDOWN && m.WParam.ToInt32() == (int)Keys.Menu)
            {
                ShowAccessibleAltMenu();
                return;
            }
            base.WndProc(ref m);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (!IsHandleCreated || !ContainsFocus) return false;
            if ((m.Msg == WM_SYSKEYDOWN || m.Msg == WM_KEYDOWN) && m.WParam.ToInt32() == (int)Keys.Menu)
            {
                ShowAccessibleAltMenu();
                return true;
            }
            return false;
        }

        private void BuildUi()
        {
            var main = new TableLayoutPanel();
            main.Dock = DockStyle.Fill;
            main.Padding = new Padding(12);
            main.ColumnCount = 1;
            main.RowCount = 8;
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(main);

            topActionsPanel = new FlowLayoutPanel();
            topActionsPanel.Dock = DockStyle.Top;
            topActionsPanel.AutoSize = true;
            topActionsPanel.Margin = new Padding(0, 0, 0, 8);
            main.Controls.Add(topActionsPanel, 0, 0);

            mainMessageLabel = new Label();
            mainMessageLabel.AutoSize = true;
            mainMessageLabel.TabStop = true;
            mainMessageLabel.AccessibleName = "Youtube Light. Pressione Alt para ir para o menu.";
            mainMessageLabel.Text = "Youtube Light. Pressione Alt para ir para o menu.";
            topActionsPanel.Controls.Add(mainMessageLabel);

            searchPanel = new TableLayoutPanel();
            searchPanel.Dock = DockStyle.Top;
            searchPanel.ColumnCount = 3;
            searchPanel.AutoSize = true;
            searchPanel.Visible = false;
            searchPanel.Margin = new Padding(0, 0, 0, 8);
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            main.Controls.Add(searchPanel, 0, 1);

            var searchLabel = new Label();
            searchLabel.Text = "Busca:";
            searchLabel.AutoSize = true;
            searchLabel.Margin = new Padding(0, 7, 8, 7);
            searchLabel.TabStop = false;
            searchPanel.Controls.Add(searchLabel, 0, 0);

            searchBox = new TextBox();
            searchBox.Dock = DockStyle.Fill;
            searchBox.Margin = new Padding(0, 4, 8, 4);
            searchBox.AccessibleName = "Campo de busca";
            searchBox.AccessibleDescription = "Digite artista, música, canal, vídeo ou playlist e pressione Enter para buscar.";
            searchPanel.Controls.Add(searchBox, 1, 0);

            var searchButton = MakeButton("Buscar agora", "Botão buscar agora", "Executa a busca digitada.");
            searchPanel.Controls.Add(searchButton, 2, 0);

            feedGroup = new GroupBox();
            feedGroup.Text = "Feed";
            feedGroup.AccessibleName = "Seção feed";
            feedGroup.Dock = DockStyle.Top;
            feedGroup.Height = 115;
            main.Controls.Add(feedGroup, 0, 2);

            feedList = new ListBox();
            feedList.Dock = DockStyle.Fill;
            feedList.AccessibleName = "Lista do feed";
            feedList.AccessibleDescription = "Use seta para cima e seta para baixo. Pressione Enter para carregar.";
            feedGroup.Controls.Add(feedList);
            PopulateFeedList();

            resultsPanel = new TableLayoutPanel();
            resultsPanel.Dock = DockStyle.Fill;
            resultsPanel.ColumnCount = 1;
            resultsPanel.RowCount = 2;
            resultsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            resultsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.Controls.Add(resultsPanel, 0, 3);

            var resultsLabel = new Label();
            resultsLabel.Text = "Resultados:";
            resultsLabel.AutoSize = true;
            resultsLabel.Margin = new Padding(0, 0, 0, 4);
            resultsLabel.TabStop = false;
            resultsPanel.Controls.Add(resultsLabel, 0, 0);

            resultsList = new ListBox();
            resultsList.Dock = DockStyle.Fill;
            resultsList.HorizontalScrollbar = true;
            resultsList.AccessibleName = "Lista de resultados";
            resultsList.AccessibleDescription = "Use as setas. Enter toca. Control D baixa. Em playlist, Enter abre a playlist.";
            resultsPanel.Controls.Add(resultsList, 0, 1);
            resultsList.ContextMenuStrip = BuildResultMenu();

            playerGroup = new GroupBox();
            playerGroup.Text = "Player";
            playerGroup.AccessibleName = "Seção player";
            playerGroup.Dock = DockStyle.Top;
            playerGroup.Height = 72;
            main.Controls.Add(playerGroup, 0, 4);

            playerList = new ListBox();
            playerList.Dock = DockStyle.Fill;
            playerList.AccessibleName = "Player";
            playerList.AccessibleDescription = "Atalhos: Alt P, P ou Espaço pausa. Setas esquerda e direita avançam ou voltam 10 segundos. Alt Shift setas cima e baixo avançam ou voltam o tempo configurado. Setas cima e baixo mudam volume. N próxima. B anterior. T tempo. V volume. L copia o link. R alterna repetição e aleatório em pasta só de áudio.";
            playerList.Items.Add("Player parado.");
            playerList.SelectedIndex = 0;
            playerList.KeyDown += PlayerListKeyDown;
            playerList.PreviewKeyDown += PlayerListPreviewKeyDown;
            playerList.ContextMenuStrip = BuildPlayerMenu();
            playerGroup.Controls.Add(playerList);

            moreGroup = new GroupBox();
            moreGroup.Text = "Mais opções";
            moreGroup.AccessibleName = "Seção mais opções";
            moreGroup.Dock = DockStyle.Top;
            moreGroup.Height = 100;
            main.Controls.Add(moreGroup, 0, 5);

            moreList = new ListBox();
            moreList.Dock = DockStyle.Fill;
            moreList.AccessibleName = "Lista de mais opções";
            moreList.AccessibleDescription = "Use seta para cima e seta para baixo. Pressione Enter para executar.";
            moreGroup.Controls.Add(moreList);
            PopulateMoreList();

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Height = 30;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AccessibleName = "Status";
            statusLabel.AccessibleRole = AccessibleRole.StatusBar;
            statusLabel.TabStop = true;
            statusLabel.Text = "Youtube Light aberto. Pressione Alt para ir para o menu.";
            main.Controls.Add(statusLabel, 0, 6);

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Top;
            progressBar.Height = 18;
            progressBar.Visible = false;
            progressBar.AccessibleName = "Progresso";
            progressBar.AccessibleDescription = "Progresso de atualização ou download.";
            progressBar.AccessibleRole = AccessibleRole.ProgressBar;
            main.Controls.Add(progressBar, 0, 7);

            searchButton.Click += delegate { Search(); };
            searchBox.KeyDown += SearchBoxKeyDown;
            feedList.KeyDown += FeedListKeyDown;
            moreList.KeyDown += MoreListKeyDown;
            resultsList.KeyDown += ResultsListKeyDown;
            resultsList.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Apps || e.KeyCode == Keys.Escape) e.IsInputKey = true;
            };
            resultsList.DoubleClick += delegate { PlaySelected(); };
            KeyDown += MainKeyDown;
            ShowHomeOnly();
        }

        private void ShowHomeOnly()
        {
            if (searchPanel != null) searchPanel.Visible = false;
            if (feedGroup != null) { feedGroup.Visible = false; feedGroup.TabStop = false; }
            if (resultsPanel != null) resultsPanel.Visible = false;
            if (playerGroup != null) { playerGroup.Visible = false; playerGroup.TabStop = false; }
            if (moreGroup != null) { moreGroup.Visible = false; moreGroup.TabStop = false; }
            if (resultsList != null) resultsList.TabStop = false;
            if (playerList != null) playerList.TabStop = false;
            if (statusLabel != null) statusLabel.TabStop = true;
            UpdateWindowTitle();
            if (mainMessageLabel != null)
            {
                mainMessageLabel.TabStop = true;
                mainMessageLabel.Text = "Youtube Light. Pressione Alt para ir para o menu.";
                mainMessageLabel.AccessibleName = mainMessageLabel.Text;
                mainMessageLabel.Focus();
            }
            ActiveControl = null;
            Focus();
        }

        private void ShowResultsOnly()
        {
            if (mainMessageLabel != null) mainMessageLabel.TabStop = false;
            if (statusLabel != null) statusLabel.TabStop = false;
            if (resultsPanel != null) resultsPanel.Visible = true;
            if (resultsList != null) { resultsList.TabStop = true; resultsList.TabIndex = 0; }
            if (playerGroup != null && !playbackStarted) playerGroup.Visible = false;
            if (playerList != null) { playerList.TabStop = playbackStarted; playerList.TabIndex = 1; }
        }

        private void ShowPlayerSection()
        {
            if (playerGroup != null) { playerGroup.Visible = true; playerGroup.TabStop = true; }
            if (resultsList != null) { resultsList.TabStop = true; resultsList.TabIndex = 0; }
            if (playerList != null) { playerList.TabStop = true; playerList.TabIndex = 1; }
            if (mainMessageLabel != null) mainMessageLabel.TabStop = false;
            if (statusLabel != null) statusLabel.TabStop = false;
        }

        private MenuStrip BuildMainMenu()
        {
            var menu = new MenuStrip();
            menu.AccessibleName = "Menu principal";
            menu.Visible = false;

            var youtube = new ToolStripMenuItem("&YouTube");
            youtube.DropDownItems.Add("Pesquisar", null, delegate { StartMainSearch(); });
            youtube.DropDownItems.Add("Recomendados", null, delegate { LoadYoutubeRecommendations(); });
            youtube.DropDownItems.Add("Inscrições", null, delegate { LoadYoutubeSubscriptions(); });
            youtube.DropDownItems.Add("Vídeos em alta", null, delegate { SearchYoutubeFull("vídeos em alta Brasil", "Vídeos"); });
            youtube.DropDownItems.Add("Abrir link", null, delegate { OpenYoutubeLink(); });

            var player = new ToolStripMenuItem("&Reprodução");
            player.DropDownItems.Add("Pausar ou retomar", null, delegate { TogglePause(); });
            player.DropDownItems.Add("Próxima música", null, delegate { PlayRelative(1); });
            player.DropDownItems.Add("Música anterior", null, delegate { PlayRelative(-1); });
            player.DropDownItems.Add("Volume atual", null, delegate { AnnounceVolume(true); });
            player.DropDownItems.Add("Tempo atual", null, delegate { AnnounceTime(); });
            player.DropDownItems.Add("Copiar link atual", null, delegate { CopyCurrentLink(); });

            var video = new ToolStripMenuItem("&Vídeo");
            video.DropDownItems.Add("Descrição", null, delegate { ShowTrackDescription(CurrentTrackForActions()); });
            video.DropDownItems.Add("Comentários", null, delegate { ShowTrackComments(CurrentTrackForActions()); });
            video.DropDownItems.Add("Capítulos", null, delegate { ShowTrackChapters(CurrentTrackForActions()); });
            video.DropDownItems.Add("Legendas", null, delegate { ShowTrackCaptions(CurrentTrackForActions()); });
            video.DropDownItems.Add("Relacionados", null, delegate { LoadRelatedVideos(CurrentTrackForActions()); });

            var settings = new ToolStripMenuItem("&Configurações");
            settings.DropDownItems.Add("Abrir configurações", null, delegate { ShowSettings(); });
            settings.DropDownItems.Add("Áudio e transmissão", null, delegate { ShowAudioSettings(); });
            settings.DropDownItems.Add("Atalhos personalizados", null, delegate { ShowShortcutSettings(); });
            settings.DropDownItems.Add("Verificar atualização", null, delegate { CheckAppUpdate(false); });

            var help = new ToolStripMenuItem("A&juda");
            help.DropDownItems.Add("Ajuda", null, delegate { ShowHelp(); });
            help.DropDownItems.Add("Sobre", null, delegate { ShowAbout(); });

            menu.Items.Add(youtube);
            menu.Items.Add(player);
            menu.Items.Add(video);
            menu.Items.Add(settings);
            menu.Items.Add(help);
            return menu;
        }

        private void ShowAccessibleAltMenu()
        {
            using (var form = new Form())
            {
                form.Text = "Menu do Youtube Light";
                form.AccessibleName = "Menu do Youtube Light";
                form.Size = new Size(620, 520);
                form.StartPosition = FormStartPosition.CenterParent;
                form.ShowInTaskbar = false;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.ControlBox = false;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.KeyPreview = true;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Menu principal recolhível";
                list.AccessibleDescription = "Use seta para cima e para baixo. Enter ou seta direita expande ou executa. Seta esquerda recolhe.";
                form.Controls.Add(list);

                Action refresh = delegate
                {
                    int selectedIndex = list.SelectedIndex;
                    list.Items.Clear();
                    AddAltCategory(list, "Busca", altSearchExpanded);
                    if (altSearchExpanded)
                    {
                        AddAltItem(list, "Pesquisar");
                        AddAltItem(list, "Pesquisar últimos por país");
                        AddAltItem(list, "Abrir link do YouTube");
                    }
                    AddAltCategory(list, "Player do PC", altPcPlayerExpanded);
                    if (altPcPlayerExpanded)
                    {
                        AddAltItem(list, "Abrir pasta de mídia");
                    }
                    AddAltCategory(list, "Conversor", altConverterExpanded);
                    if (altConverterExpanded)
                    {
                        AddAltItem(list, "Converter áudio ou vídeo");
                        AddAltItem(list, "Converter áudio para vídeo");
                    }
                    AddAltCategory(list, "Reprodução", altPlaybackExpanded);
                    if (altPlaybackExpanded)
                    {
                        AddAltItem(list, "Pausar ou retomar");
                        AddAltItem(list, "Próxima música");
                        AddAltItem(list, "Música anterior");
                        AddAltItem(list, "Volume atual");
                        AddAltItem(list, "Tempo atual");
                    }
                    AddAltCategory(list, "Configurações", altSettingsExpanded);
                    if (altSettingsExpanded)
                    {
                        AddAltItem(list, "Abrir configurações");
                        AddAltItem(list, "Áudio e transmissão");
                        AddAltItem(list, "Atalhos personalizados");
                        AddAltItem(list, "Escolher como player padrão do Windows");
                        AddAltItem(list, "Verificar atualização");
                    }
                    AddAltCategory(list, "Mais opções", altMoreExpanded);
                    if (altMoreExpanded)
                    {
                        AddAltItem(list, musicOnlyMode ? "Mudar para YouTube completo" : "Mudar para YouTube Music");
                        AddAltItem(list, IsLoggedIn() ? "Deslogar conta" : "Logar com Google, Alt 2");
                        AddAltItem(list, "Informações da conta");
                        AddAltItem(list, "Verificar atualização do aplicativo");
                        AddAltItem(list, "Atualizar dependências");
                        AddAltItem(list, "Diagnóstico do aplicativo");
                        AddAltItem(list, "Escolher pasta de downloads");
                        AddAltItem(list, "Usar pasta padrão de downloads");
                        AddAltItem(list, "Abrir pasta de downloads");
                        AddAltItem(list, "Dar ideias");
                        AddAltItem(list, "Sobre o aplicativo");
                        AddAltItem(list, "Ajuda");
                        AddAltItem(list, "Sair");
                    }
                    if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(selectedIndex, list.Items.Count - 1));
                };

                Action execute = delegate
                {
                    if (list.SelectedItem == null) return;
                    string item = list.SelectedItem.ToString();
                    if (ToggleAltCategory(item)) { refresh(); return; }
                    string clean = item.Trim();
                    form.Close();
                    ExecuteAltMenuItem(clean);
                };

                refresh();
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Right)
                    {
                        e.SuppressKeyPress = true;
                        execute();
                    }
                    else if (e.KeyCode == Keys.Left)
                    {
                        e.SuppressKeyPress = true;
                        if (list.SelectedItem != null && CollapseAltCategory(list.SelectedItem.ToString())) refresh();
                    }
                    else if (e.KeyCode == Keys.Escape)
                    {
                        e.SuppressKeyPress = true;
                        form.Close();
                    }
                };
                form.Shown += delegate { list.Focus(); };
                form.ShowDialog(this);
            }
        }

        private void AddAltCategory(ListBox list, string title, bool expanded)
        {
            list.Items.Add("Menu " + title + ", " + (expanded ? "expandido" : "recolhido"));
        }

        private void AddAltItem(ListBox list, string title)
        {
            list.Items.Add("  " + title);
        }

        private bool ToggleAltCategory(string item)
        {
            if (String.IsNullOrWhiteSpace(item) || !item.StartsWith("Menu ", StringComparison.OrdinalIgnoreCase)) return false;
            if (item.StartsWith("Menu Busca", StringComparison.OrdinalIgnoreCase)) altSearchExpanded = !altSearchExpanded;
            else if (item.StartsWith("Menu Player do PC", StringComparison.OrdinalIgnoreCase)) altPcPlayerExpanded = !altPcPlayerExpanded;
            else if (item.StartsWith("Menu Conversor", StringComparison.OrdinalIgnoreCase)) altConverterExpanded = !altConverterExpanded;
            else if (item.StartsWith("Menu Reprodução", StringComparison.OrdinalIgnoreCase)) altPlaybackExpanded = !altPlaybackExpanded;
            else if (item.StartsWith("Menu Configurações", StringComparison.OrdinalIgnoreCase)) altSettingsExpanded = !altSettingsExpanded;
            else if (item.StartsWith("Menu Mais opções", StringComparison.OrdinalIgnoreCase)) altMoreExpanded = !altMoreExpanded;
            else return false;
            return true;
        }

        private bool CollapseAltCategory(string item)
        {
            if (String.IsNullOrWhiteSpace(item)) return false;
            if (item.StartsWith("Menu Busca", StringComparison.OrdinalIgnoreCase)) altSearchExpanded = false;
            else if (item.StartsWith("Menu Player do PC", StringComparison.OrdinalIgnoreCase)) altPcPlayerExpanded = false;
            else if (item.StartsWith("Menu Conversor", StringComparison.OrdinalIgnoreCase)) altConverterExpanded = false;
            else if (item.StartsWith("Menu Reprodução", StringComparison.OrdinalIgnoreCase)) altPlaybackExpanded = false;
            else if (item.StartsWith("Menu Configurações", StringComparison.OrdinalIgnoreCase)) altSettingsExpanded = false;
            else if (item.StartsWith("Menu Mais opções", StringComparison.OrdinalIgnoreCase)) altMoreExpanded = false;
            else return false;
            return true;
        }

        private void ExecuteAltMenuItem(string item)
        {
            if (item == "Pesquisar") FullYoutubeSearch();
            else if (item == "Pesquisar últimos por país") SearchByCountryDialog();
            else if (item == "Abrir link do YouTube") OpenYoutubeLink();
            else if (item == "Abrir pasta de mídia") LoadLocalMediaFolder(false);
            else if (item == "Converter áudio ou vídeo") ConvertMediaFile(false);
            else if (item == "Converter áudio para vídeo") ConvertMediaFile(true);
            else if (item == "Pausar ou retomar") TogglePause();
            else if (item == "Próxima música") PlayRelative(1);
            else if (item == "Música anterior") PlayRelative(-1);
            else if (item == "Volume atual") AnnounceVolume(true);
            else if (item == "Tempo atual") AnnounceTime();
            else if (item == "Abrir configurações") ShowSettings();
            else if (item == "Áudio e transmissão") ShowAudioSettings();
            else if (item == "Atalhos personalizados") ShowShortcutSettings();
            else if (item == "Escolher como player padrão do Windows") OpenDefaultAppsSettings();
            else if (item == "Verificar atualização") CheckAppUpdate(false);
            else if (item == "Mudar para YouTube Music" || item == "Mudar para YouTube completo") ToggleMusicOnlyMode();
            else if (item == "Logar com Google, Alt 2") BrowserLogin();
            else if (item == "Deslogar conta") Logout();
            else if (item == "Informações da conta") LoadInfo("account");
            else if (item == "Verificar atualização do aplicativo") CheckAppUpdate(false);
            else if (item == "Atualizar dependências") UpdateDependencies(false);
            else if (item == "Diagnóstico do aplicativo") RunDiagnostics();
            else if (item == "Escolher pasta de downloads") ChooseDownloadFolder();
            else if (item == "Usar pasta padrão de downloads") UseDefaultDownloadFolder();
            else if (item == "Abrir pasta de downloads") Process.Start(GetDownloadDir());
            else if (item == "Dar ideias") OpenIdeasEmail();
            else if (item == "Sobre o aplicativo") ShowAbout();
            else if (item == "Sair") Close();
            else if (item == "Ajuda") ShowHelp();
            else if (item == "Sobre o aplicativo") ShowAbout();
        }
        private Button MakeButton(string text, string accessibleName, string accessibleDescription)
        {
            var button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 8, 6);
            button.AccessibleName = accessibleName;
            button.AccessibleDescription = accessibleDescription;
            return button;
        }

        private Button AddButton(FlowLayoutPanel panel, string text, string name, string description, EventHandler handler)
        {
            var button = MakeButton(text, name, description);
            button.Click += handler;
            panel.Controls.Add(button);
            return button;
        }

        private ContextMenuStrip BuildResultMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Opening += delegate
            {
                menu.Items.Clear();
                Track track = SelectedTrack();
                if (track == null)
                {
                    menu.Items.Add("Nenhuma música selecionada").Enabled = false;
                    return;
                }

                if (track.LikeStatus == "LIKE")
                {
                    menu.Items.Add("Remover curtida", null, delegate { RateSelected("INDIFFERENT"); });
                    menu.Items.Add("Descurtir", null, delegate { RateSelected("DISLIKE"); });
                }
                else if (track.LikeStatus == "DISLIKE")
                {
                    menu.Items.Add("Curtir", null, delegate { RateSelected("LIKE"); });
                    menu.Items.Add("Remover descurtida", null, delegate { RateSelected("INDIFFERENT"); });
                }
                else
                {
                    menu.Items.Add("Curtir", null, delegate { RateSelected("LIKE"); });
                    menu.Items.Add("Descurtir", null, delegate { RateSelected("DISLIKE"); });
                }

                menu.Items.Add("Tocar a seguir", null, delegate { QueueSelected(true); });
                menu.Items.Add("Adicionar ao fim da fila", null, delegate { QueueSelected(false); });
                menu.Items.Add("Adicionar aos favoritos locais", null, delegate { AddSelectedToLocalFavorites(); });
                menu.Items.Add("Remover dos favoritos locais", null, delegate { RemoveSelectedFromLocalFavorites(); });
                menu.Items.Add("Adicionar a playlist", null, delegate { AddSelectedToPlaylist(); });
                menu.Items.Add("Abrir rádio desta música", null, delegate { LoadRadioFromTrack(track); });
                menu.Items.Add("Baixar como áudio", null, delegate { DownloadSelectedAsAudio(); });
                menu.Items.Add("Baixar como vídeo", null, delegate { DownloadSelectedAsVideo(); });
                menu.Items.Add("Copiar link", null, delegate
                {
                    string url = TrackUrl(track);
                    if (!String.IsNullOrWhiteSpace(url))
                    {
                        Clipboard.SetText(url);
                        SetStatus("Link copiado.");
                    }
                });
            };
            return menu;
        }

        private ContextMenuStrip BuildPlayerMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Opening += delegate
            {
                menu.Items.Clear();
                Track track = CurrentTrackForActions();
                if (track == null)
                {
                    menu.Items.Add("Nenhuma música tocando").Enabled = false;
                    return;
                }
                AddTrackActionItems(menu, track);
                menu.Items.Add("Trocar saída principal ou transmissão", null, delegate { ChooseOutputDevice(); });
                menu.Items.Add("Escolher retorno do player no fone", null, delegate { ChooseMonitorOutputDevice(); });
            };
            return menu;
        }

        private void AddTrackActionItems(ContextMenuStrip menu, Track track)
        {
            if (track.LikeStatus == "LIKE")
            {
                menu.Items.Add("Remover curtida", null, delegate { RateTrack(track, "INDIFFERENT"); });
                menu.Items.Add("Descurtir", null, delegate { RateTrack(track, "DISLIKE"); });
            }
            else if (track.LikeStatus == "DISLIKE")
            {
                menu.Items.Add("Curtir", null, delegate { RateTrack(track, "LIKE"); });
                menu.Items.Add("Remover descurtida", null, delegate { RateTrack(track, "INDIFFERENT"); });
            }
            else
            {
                menu.Items.Add("Curtir", null, delegate { RateTrack(track, "LIKE"); });
                menu.Items.Add("Descurtir", null, delegate { RateTrack(track, "DISLIKE"); });
            }

            menu.Items.Add("Tocar a seguir", null, delegate { QueueTrack(track, true); });
            menu.Items.Add("Adicionar ao fim da fila", null, delegate { QueueTrack(track, false); });
            menu.Items.Add("Adicionar aos favoritos locais", null, delegate { AddTrackToLocalFavorites(track); });
            menu.Items.Add("Remover dos favoritos locais", null, delegate { RemoveTrackFromLocalFavorites(track); });
            menu.Items.Add("Adicionar a playlist", null, delegate { AddTrackToPlaylist(track); });
            menu.Items.Add("Abrir rádio desta música", null, delegate { LoadRadioFromTrack(track); });
            menu.Items.Add("Baixar como áudio", null, delegate { DownloadTrackAsAudio(track); });
            menu.Items.Add("Baixar como vídeo", null, delegate { DownloadTrackAsVideo(track); });
            menu.Items.Add("Copiar link", null, delegate
            {
                string url = TrackUrl(track);
                if (!String.IsNullOrWhiteSpace(url))
                {
                    Clipboard.SetText(url);
                    SetStatus("Link copiado.");
                }
            });
            menu.Items.Add("Descrição do vídeo", null, delegate { ShowTrackDescription(track); });
            menu.Items.Add("Comentários", null, delegate { ShowTrackComments(track); });
            menu.Items.Add("Capítulos", null, delegate { ShowTrackChapters(track); });
            menu.Items.Add("Legendas", null, delegate { ShowTrackCaptions(track); });
            menu.Items.Add("Vídeos relacionados", null, delegate { LoadRelatedVideos(track); });
        }

        private void PopulateFeedList()
        {
            if (feedList == null) return;
            feedList.Items.Clear();
            if (!musicOnlyMode)
            {
                AddFeedCategory("Buscar", feedSearchExpanded);
                if (feedSearchExpanded)
                {
                    AddFeedItem("Pesquisar no YouTube");
                    AddFeedItem("Abrir link do YouTube");
                }
                AddFeedCategory("Conta", feedAccountExpanded);
                if (feedAccountExpanded)
                {
                    AddFeedItem("Recomendados do YouTube");
                    AddFeedItem("Inscrições do YouTube");
                }
                AddFeedCategory("Explorar", feedExploreExpanded);
                if (feedExploreExpanded)
                {
                    AddFeedItem("Vídeos em alta");
                    AddFeedItem("Playlists");
                    AddFeedItem("Canais");
                }
                AddFeedCategory("Biblioteca local", feedLibraryExpanded);
                if (feedLibraryExpanded)
                {
                    AddFeedItem("Fila de reprodução");
                    AddFeedItem("Favoritos locais");
                    AddFeedItem("Histórico local");
                    AddFeedItem("Downloads");
                }
                if (feedList.Items.Count > 0) feedList.SelectedIndex = 0;
                return;
            }
            feedList.Items.Add("Recomendadas");
            feedList.Items.Add("Busca avançada");
            feedList.Items.Add("Ouvir de novo");
            feedList.Items.Add("Playlists");
            feedList.Items.Add("Fila de reprodução");
            feedList.Items.Add("Favoritos locais");
            feedList.Items.Add("Histórico local");
            feedList.Items.Add("Rádio da música selecionada");
            feedList.Items.Add("Curtidas");
            feedList.Items.Add("Histórico");
            feedList.Items.Add("Biblioteca");
            feedList.Items.Add("Charts BR");
            feedList.Items.Add("Explorar");
            feedList.Items.Add("Letra da música selecionada");
            if (feedList.Items.Count > 0) feedList.SelectedIndex = 0;
        }

        private void AddFeedCategory(string title, bool expanded)
        {
            feedList.Items.Add("Categoria " + title + ", " + (expanded ? "expandida" : "recolhida"));
        }

        private void AddFeedItem(string title)
        {
            feedList.Items.Add("  " + title);
        }

        private string CleanFeedItem(string item)
        {
            return (item ?? "").Trim();
        }

        private bool ToggleFeedCategoryIfNeeded(string item)
        {
            if (String.IsNullOrWhiteSpace(item) || !item.StartsWith("Categoria ", StringComparison.OrdinalIgnoreCase)) return false;
            if (item.StartsWith("Categoria Buscar", StringComparison.OrdinalIgnoreCase)) feedSearchExpanded = !feedSearchExpanded;
            else if (item.StartsWith("Categoria Conta", StringComparison.OrdinalIgnoreCase)) feedAccountExpanded = !feedAccountExpanded;
            else if (item.StartsWith("Categoria Explorar", StringComparison.OrdinalIgnoreCase)) feedExploreExpanded = !feedExploreExpanded;
            else if (item.StartsWith("Categoria Biblioteca local", StringComparison.OrdinalIgnoreCase)) feedLibraryExpanded = !feedLibraryExpanded;
            PopulateFeedList();
            SetStatus(item.Contains("recolhida") ? "Categoria expandida." : "Categoria recolhida.");
            return true;
        }

        private void PopulateMoreList()
        {
            if (moreList == null) return;
            moreList.Items.Clear();
            moreList.Items.Add(musicOnlyMode ? "Mudar para YouTube completo" : "Mudar para YouTube Music");
            moreList.Items.Add(IsLoggedIn() ? "Deslogar conta" : "Logar com Google, Alt 2");
            moreList.Items.Add("Informações da conta");
            moreList.Items.Add("Verificar atualização do aplicativo");
            moreList.Items.Add("Atualizar dependências");
            moreList.Items.Add("Configurações");
            moreList.Items.Add("Diagnóstico do aplicativo");
            moreList.Items.Add("Escolher pasta de downloads");
            moreList.Items.Add("Usar pasta padrão de downloads");
            moreList.Items.Add("Abrir pasta de downloads");
            moreList.Items.Add("Dar ideias");
            moreList.Items.Add("Sobre o aplicativo");
            moreList.Items.Add("Ajuda");
            moreList.Items.Add("Sair");
            if (moreList.Items.Count > 0) moreList.SelectedIndex = 0;
        }

        private void ShowSearchBox()
        {
            searchPanel.Visible = true;
            searchBox.Focus();
            searchBox.SelectAll();
            SetStatus("Digite a busca e pressione Enter.");
        }

        private void StartMainSearch()
        {
            if (musicOnlyMode) ShowSearchBox();
            else FullYoutubeSearch();
        }

        private void ToggleMusicOnlyMode()
        {
            musicOnlyMode = !musicOnlyMode;
            SaveConfig();
            PopulateFeedList();
            PopulateMoreList();
            AnnounceStatus(musicOnlyMode ? "Modo YouTube Music ativado." : "Modo YouTube completo ativado.");
        }

        private void FeedListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ExecuteFeedItem();
            }
        }

        private void MoreListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ExecuteMoreItem();
            }
        }

        private void ExecuteFeedItem()
        {
            if (feedList == null || feedList.SelectedItem == null) return;
            string item = CleanFeedItem(feedList.SelectedItem.ToString());
            if (ToggleFeedCategoryIfNeeded(item)) return;
            if (!musicOnlyMode)
            {
                if (item == "Pesquisar no YouTube") FullYoutubeSearch();
                else if (item == "Recomendados do YouTube") LoadYoutubeRecommendations();
                else if (item == "Inscrições do YouTube") LoadYoutubeSubscriptions();
                else if (item == "Vídeos em alta") SearchYoutubeFull("vídeos em alta Brasil", "Vídeos");
                else if (item == "Playlists") SearchYoutubeFull("playlist Brasil", "Playlists");
                else if (item == "Canais") SearchYoutubeFull("canal oficial Brasil", "Canais");
                else if (item == "Fila de reprodução") ReplaceList(new List<Track>(playbackQueue), "Fila de reprodução carregada.");
                else if (item == "Favoritos locais") ReplaceList(new List<Track>(localFavorites), "Favoritos locais carregados.");
                else if (item == "Histórico local") ReplaceList(new List<Track>(localHistory), "Histórico local carregado.");
                else if (item == "Downloads") Process.Start(GetDownloadDir());
                else if (item == "Abrir link do YouTube") OpenYoutubeLink();
                return;
            }

            if (item == "Recomendadas") LoadBridgeList("home", "Carregando recomendadas.");
            else if (item == "Busca avançada") AdvancedSearch();
            else if (item == "Ouvir de novo") LoadBridgeList("listen_again", "Carregando ouvir de novo.");
            else if (item == "Playlists") LoadBridgeList("playlists", "Carregando playlists.");
            else if (item == "Fila de reprodução") ReplaceList(new List<Track>(playbackQueue), "Fila de reprodução carregada.");
            else if (item == "Favoritos locais") ReplaceList(new List<Track>(localFavorites), "Favoritos locais carregados.");
            else if (item == "Histórico local") ReplaceList(new List<Track>(localHistory), "Histórico local carregado.");
            else if (item == "Rádio da música selecionada") LoadRadio();
            else if (item == "Curtidas") LoadBridgeList("liked", "Carregando curtidas.");
            else if (item == "Histórico") LoadBridgeList("history", "Carregando histórico.");
            else if (item == "Biblioteca") LoadBridgeList("library_songs", "Carregando biblioteca.");
            else if (item == "Charts BR") LoadBridgeList("charts", "Carregando charts do Brasil.");
            else if (item == "Explorar") LoadBridgeList("explore", "Carregando explorar.");
            else if (item == "Letra da música selecionada") ShowLyrics();
        }

        private void ExecuteMoreItem()
        {
            if (moreList == null || moreList.SelectedItem == null) return;
            string item = moreList.SelectedItem.ToString();
            if (item == "Mudar para YouTube Music" || item == "Mudar para YouTube completo") ToggleMusicOnlyMode();
            else if (item == "Logar com Google, Alt 2") BrowserLogin();
            else if (item == "Deslogar conta") Logout();
            else if (item == "Informações da conta") LoadInfo("account");
            else if (item == "Verificar atualização do aplicativo") CheckAppUpdate(false);
            else if (item == "Atualizar dependências") UpdateDependencies(false);
            else if (item == "Configurações") ShowSettings();
            else if (item == "Diagnóstico do aplicativo") RunDiagnostics();
            else if (item == "Escolher pasta de downloads") ChooseDownloadFolder();
            else if (item == "Usar pasta padrão de downloads") UseDefaultDownloadFolder();
            else if (item == "Abrir pasta de downloads") Process.Start(GetDownloadDir());
            else if (item == "Dar ideias") OpenIdeasEmail();
            else if (item == "Sobre o aplicativo") ShowAbout();
            else if (item == "Ajuda") ShowHelp();
            else if (item == "Sair") Close();
        }

        private void SearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Search();
            }
        }

        private void ResultsListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                PlaySelected();
            }
        }

        private void PlayerListPreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                e.KeyCode == Keys.Space || e.KeyCode == Keys.P || e.KeyCode == Keys.V || e.KeyCode == Keys.T ||
                e.KeyCode == Keys.L || e.KeyCode == Keys.N || e.KeyCode == Keys.B || e.KeyCode == Keys.R)
                e.IsInputKey = true;
        }

        private void PlayerListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Apps || e.KeyData == (Keys.Shift | Keys.F10))
            {
                e.SuppressKeyPress = true;
                if (playerList.ContextMenuStrip != null)
                    playerList.ContextMenuStrip.Show(playerList, new Point(20, 20));
                return;
            }
            if (HandlePlayerKey(e.KeyData))
                e.SuppressKeyPress = true;
        }

        private bool HandlePlayerKey(Keys keyData)
        {
            string action = ActionFromShortcut(keyData);
            if (!String.IsNullOrWhiteSpace(action))
            {
                ExecuteShortcutAction(action);
                return true;
            }
            if (keyData == Keys.P || keyData == Keys.Space)
            {
                TogglePause();
                return true;
            }
            if (keyData == Keys.R)
            {
                CycleLocalFolderPlaybackMode();
                return true;
            }
            if (keyData == (Keys.Shift | Keys.T))
            {
                AnnounceTitle();
                return true;
            }
            if (keyData == (Keys.Alt | Keys.Shift | Keys.Right))
            {
                ExecuteShortcutAction("seekForward");
                return true;
            }
            if (keyData == (Keys.Alt | Keys.Shift | Keys.Left))
            {
                ExecuteShortcutAction("seekBack");
                return true;
            }
            if (keyData == Keys.Left)
            {
                SendPlayerCommand("seek", "-10");
                AnnouncePlayerEvent("Voltando 10 segundos.");
                return true;
            }
            if (keyData == Keys.Right)
            {
                SendPlayerCommand("seek", "10");
                AnnouncePlayerEvent("Avançando 10 segundos.");
                return true;
            }
            if (keyData == Keys.Up || keyData == (Keys.Alt | Keys.Shift | Keys.Up))
            {
                ExecuteShortcutAction("volumeUp");
                return true;
            }
            if (keyData == Keys.Down || keyData == (Keys.Alt | Keys.Shift | Keys.Down))
            {
                ExecuteShortcutAction("volumeDown");
                return true;
            }
            return false;
        }

        private string ActionFromShortcut(Keys keyData)
        {
            foreach (var item in customShortcuts)
                if (item.Value == keyData && IsPlayerShortcutAction(item.Key)) return item.Key;
            if (keyData == Keys.V) return "volume";
            if (keyData == Keys.T) return "time";
            if (keyData == Keys.L) return "link";
            if (keyData == Keys.N) return "next";
            if (keyData == Keys.B) return "previous";
            if (keyData == (Keys.Alt | Keys.P)) return "pause";
            return "";
        }

        private void ExecuteShortcutAction(string action)
        {
            if (action == "pause") TogglePause();
            else if (action == "seekBack")
            {
                SendPlayerCommand("seek", "-" + altShiftSeekSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AnnouncePlayerEvent("Voltando " + altShiftSeekSeconds + " segundos.");
            }
            else if (action == "seekForward")
            {
                SendPlayerCommand("seek", altShiftSeekSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AnnouncePlayerEvent("Avançando " + altShiftSeekSeconds + " segundos.");
            }
            else if (action == "volumeUp")
            {
                SendPlayerCommand("add", "volume", "5");
                AnnounceVolume(announcePlayerEvents);
            }
            else if (action == "volumeDown")
            {
                SendPlayerCommand("add", "volume", "-5");
                AnnounceVolume(announcePlayerEvents);
            }
            else if (action == "volume") AnnounceVolume(true);
            else if (action == "time") AnnounceTime();
            else if (action == "link") CopyCurrentLink();
            else if (action == "next") PlayRelative(1);
            else if (action == "previous") PlayRelative(-1);
        }

        private bool IsPlayerShortcutAction(string action)
        {
            return action == "pause" || action == "seekBack" || action == "seekForward" ||
                action == "volumeUp" || action == "volumeDown" || action == "volume" ||
                action == "time" || action == "link" ||
                action == "next" || action == "previous";
        }

        private bool HasShortcutModifier(Keys keyData)
        {
            return (keyData & Keys.Alt) == Keys.Alt ||
                (keyData & Keys.Control) == Keys.Control ||
                (keyData & Keys.Shift) == Keys.Shift;
        }

        private bool IsGlobalPlayerShortcut(Keys keyData)
        {
            if (!HasShortcutModifier(keyData)) return false;
            return keyData == Keys.Shift || keyData == Keys.Control || keyData == Keys.Alt ? false :
                keyData == (Keys.Alt | Keys.P) ||
                keyData == (Keys.Alt | Keys.Shift | Keys.Left) || keyData == (Keys.Alt | Keys.Shift | Keys.Right) ||
                keyData == (Keys.Alt | Keys.Shift | Keys.Up) || keyData == (Keys.Alt | Keys.Shift | Keys.Down) ||
                keyData == (Keys.Alt | Keys.Shift | Keys.N) || keyData == (Keys.Alt | Keys.Shift | Keys.B) ||
                keyData == (Keys.Shift | Keys.T) ||
                (!String.IsNullOrWhiteSpace(ActionFromShortcut(keyData)) && HasShortcutModifier(keyData));
        }

        private void MainKeyDown(object sender, KeyEventArgs e)
        {
            bool inTextBox = ActiveControl is TextBox;
            bool inResults = IsResultsHotkeyContext();
            bool inPlayer = ActiveControl == playerList;
            if (e.KeyData == customShortcuts["search"] || e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.L || e.KeyCode == Keys.P))
            {
                StartMainSearch();
                e.SuppressKeyPress = true;
            }
            else if (inPlayer && HandlePlayerKey(e.KeyData)) { e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.S) { StopPlayback(); e.SuppressKeyPress = true; }
            else if (inResults && e.Control && e.Shift && e.KeyCode == Keys.B) { DownloadSelectedAsVideo(); e.SuppressKeyPress = true; }
            else if (inResults && e.Control && e.KeyCode == Keys.B) { DownloadSelectedAsAudio(); e.SuppressKeyPress = true; }
            else if (inPlayer && e.Control && e.Shift && e.KeyCode == Keys.B) { DownloadTrackAsVideo(CurrentTrackForActions()); e.SuppressKeyPress = true; }
            else if (inPlayer && e.Control && e.KeyCode == Keys.B) { DownloadTrackAsAudio(CurrentTrackForActions()); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.D) { DownloadSelected(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Right) { PlayRelative(1); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Left) { PlayRelative(-1); e.SuppressKeyPress = true; }
            else if (!inTextBox && IsGlobalPlayerShortcut(e.KeyData) && HandlePlayerKey(e.KeyData)) { e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { UpdateDependencies(false); e.SuppressKeyPress = true; }
        }

        private void Search()
        {
            string query = searchBox.Text.Trim();
            if (query.Length == 0) { SetStatus("Digite uma busca primeiro."); return; }
            if (!musicOnlyMode)
            {
                SearchYoutubeFull(query, "Tudo");
                return;
            }
            AnnounceStatus("Buscando por " + query + ", por favor aguarde.");
            RunWorker(delegate
            {
                try { ReplaceList(TracksFromBridge("search", query), "Resultados do YouTube Music."); }
                catch { ReplaceList(SearchWithYtdlp(query), "Resultados de busca por yt-dlp."); }
            });
        }

        private void AdvancedSearch()
        {
            using (var form = new Form())
            {
                form.Text = "Busca avançada";
                form.Size = new Size(520, 240);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);
                var typeList = new ComboBox();
                typeList.DropDownStyle = ComboBoxStyle.DropDownList;
                typeList.AccessibleName = "Tipo de busca";
                typeList.Items.AddRange(new object[] { "Música", "Vídeo", "Álbum", "Playlist", "Artista" });
                typeList.SelectedIndex = 0;
                panel.Controls.Add(typeList, 0, 0);
                var queryBox = new TextBox();
                queryBox.AccessibleName = "Texto da busca";
                queryBox.Dock = DockStyle.Top;
                panel.Controls.Add(queryBox, 0, 1);
                var ok = new Button { Text = "Buscar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string query = queryBox.Text.Trim();
                if (String.IsNullOrWhiteSpace(query)) { SetStatus("Digite uma busca primeiro."); return; }
                string filter = typeList.SelectedItem.ToString().ToLowerInvariant();
                if (filter == "música") filter = "songs";
                else if (filter == "vídeo") filter = "videos";
                else if (filter == "álbum") filter = "albums";
                else if (filter == "playlist") filter = "playlists";
                else if (filter == "artista") filter = "artists";
                AnnounceStatus("Buscando por " + query + " em " + typeList.SelectedItem + ", por favor aguarde.");
                RunWorker(delegate
                {
                    try { ReplaceList(TracksFromBridge("search_filter", query, filter), "Busca avançada carregada."); }
                    catch { ReplaceList(SearchWithYtdlp(query + " " + typeList.SelectedItem), "Busca avançada por yt-dlp."); }
                });
            }
        }

        private void FullYoutubeSearch()
        {
            using (var form = new Form())
            {
                form.Text = "Pesquisar no YouTube";
                form.Size = new Size(620, 260);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 4;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);
                var label = new Label();
                label.Text = "Digite o que deseja pesquisar.";
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);
                var queryBox = new TextBox();
                queryBox.AccessibleName = "Texto da pesquisa";
                queryBox.AccessibleDescription = "Digite o que deseja pesquisar.";
                queryBox.Dock = DockStyle.Top;
                panel.Controls.Add(queryBox, 0, 1);
                var filterList = new ComboBox();
                filterList.DropDownStyle = ComboBoxStyle.DropDownList;
                filterList.AccessibleName = "Filtro da pesquisa";
                filterList.Items.AddRange(new object[] { "Vídeos", "Músicas", "Playlists", "Canais" });
                filterList.SelectedIndex = 0;
                panel.Controls.Add(filterList, 0, 2);
                var ok = new Button { Text = "Pesquisar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 3);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string query = queryBox.Text.Trim();
                if (String.IsNullOrWhiteSpace(query)) { SetStatus("Digite uma busca primeiro."); return; }
                SearchYoutubeFull(query, filterList.SelectedItem.ToString());
            }
        }

        private object[] YoutubeCountries()
        {
            return new object[]
            {
                "Sem país específico",
                "Brasil",
                "Portugal",
                "Estados Unidos",
                "Reino Unido",
                "Espanha",
                "México",
                "Argentina",
                "França",
                "Alemanha",
                "Japão",
                "Coreia do Sul"
            };
        }

        private string CountrySearchText(string country)
        {
            if (String.IsNullOrWhiteSpace(country) || country == "Sem país específico") return "";
            return country;
        }

        private void SearchByCountryDialog()
        {
            using (var form = new Form())
            {
                form.Text = "Pesquisar últimos por país";
                form.Size = new Size(560, 260);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);
                var countryList = new ComboBox();
                countryList.DropDownStyle = ComboBoxStyle.DropDownList;
                countryList.AccessibleName = "País";
                countryList.Items.AddRange(YoutubeCountries().Skip(1).ToArray());
                countryList.SelectedIndex = 0;
                panel.Controls.Add(countryList, 0, 0);
                var typeList = new ComboBox();
                typeList.DropDownStyle = ComboBoxStyle.DropDownList;
                typeList.AccessibleName = "Tipo";
                typeList.Items.AddRange(new object[] { "Vídeos recentes", "Músicas recentes" });
                typeList.SelectedIndex = 0;
                panel.Controls.Add(typeList, 0, 1);
                var ok = new Button { Text = "Pesquisar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string country = CountrySearchText(countryList.SelectedItem.ToString());
                string type = typeList.SelectedItem.ToString();
                string query = type + " " + country;
                SearchYoutubeFull(query, type.StartsWith("Músicas") ? "Músicas" : "Vídeos");
            }
        }

        private string ChooseYoutubeSearchFilter()
        {
            using (var form = new Form())
            {
                form.Text = "Tipo de pesquisa";
                form.Size = new Size(420, 260);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Tipo de pesquisa";
                list.AccessibleDescription = "Use as setas e pressione Enter.";
                list.Items.Add("Músicas");
                list.Items.Add("Playlists");
                list.Items.Add("Canais");
                list.SelectedIndex = 0;
                form.Controls.Add(list);
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                };
                if (form.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null)
                    return list.SelectedItem.ToString();
            }
            return "";
        }

        private void SearchYoutubeFull(string query, string filter)
        {
            if (String.IsNullOrWhiteSpace(query)) { SetStatus("Digite uma busca primeiro."); return; }
            AnnounceStatus("Buscando por " + query + ", por favor aguarde.");
            RunWorker(delegate { ReplaceList(SearchYoutubeFullWithYtdlp(query, filter), "Resultados do YouTube completo."); });
        }

        private List<Track> SearchYoutubeFullWithYtdlp(string query)
        {
            return SearchYoutubeFullWithYtdlp(query, "Tudo");
        }

        private List<Track> SearchYoutubeFullWithYtdlp(string query, string filter)
        {
            string normalized = (filter ?? "Tudo").ToLowerInvariant();
            if (normalized == "músicas" || normalized == "musicas")
            {
                query += " música";
            }
            if (normalized == "playlists")
            {
                query += " playlist";
            }
            if (normalized == "canais") query += " canal oficial";

            string output = RunYtdlp(GetYtdlpYoutubeArgs() + "--dump-json --flat-playlist --default-search ytsearch20 \"ytsearch20:" + EscapeArg(query) + "\"", 90000);
            string kind = normalized == "canais" ? "channel" : normalized == "playlists" ? "playlist" : "track";
            return TracksFromYtdlpJsonLines(output, kind);
        }

        private void LoadYoutubeRecommendations()
        {
            SetStatus("Carregando recomendados do YouTube.");
            RunWorker(delegate
            {
                var found = TryLoadYoutubePageWithYtdlp("https://www.youtube.com/");
                if (found.Count == 0)
                {
                    try { found = TracksFromBridge("home"); }
                    catch { found = SearchYoutubeFullWithYtdlp("recomendados Brasil", "Vídeos"); }
                }
                ReplaceList(found, "Recomendados do YouTube carregados.");
            });
        }

        private void LoadYoutubeSubscriptions()
        {
            SetStatus("Carregando inscrições do YouTube.");
            RunWorker(delegate
            {
                var found = TryLoadYoutubePageWithYtdlp("https://www.youtube.com/feed/subscriptions");
                if (found.Count == 0)
                {
                    try { found = TracksFromBridge("subscriptions"); }
                    catch { }
                }
                ReplaceList(found, "Inscrições do YouTube carregadas.");
            });
        }

        private List<Track> TryLoadYoutubePageWithYtdlp(string url)
        {
            try
            {
                string target = "\"" + EscapeArg(url) + "\"";
                string args = GetYtdlpCookieArgs() + GetYtdlpYoutubeArgs() + "--dump-json --flat-playlist --playlist-end 40 " + target;
                return TracksFromYtdlpJsonLines(RunYtdlp(args, 120000), "track");
            }
            catch
            {
                return new List<Track>();
            }
        }

        private List<Track> TracksFromYtdlpJsonLines(string output, string forcedKind)
        {
            var found = new List<Track>();
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var data = serializer.Deserialize<Dictionary<string, object>>(line);
                found.Add(TrackFromYtdlpDictionary(data, forcedKind));
            }
            return found;
        }

        private Track TrackFromYtdlpDictionary(Dictionary<string, object> data, string forcedKind)
        {
            string id = GetString(data, "id");
            string url = GetString(data, "webpage_url");
            if (String.IsNullOrEmpty(url)) url = GetString(data, "url");
            if (forcedKind == "channel")
            {
                string channelUrl = GetString(data, "channel_url", GetString(data, "uploader_url", ""));
                if (!String.IsNullOrWhiteSpace(channelUrl)) url = channelUrl;
            }
            if (!String.IsNullOrEmpty(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://www.youtube.com/watch?v=" + url;
            if (String.IsNullOrWhiteSpace(url) && !String.IsNullOrWhiteSpace(id))
                url = "https://www.youtube.com/watch?v=" + id;
            string title = GetString(data, "title", "Sem título");
            string channel = GetString(data, "channel", GetString(data, "uploader", ""));
            if (forcedKind == "channel")
            {
                title = String.IsNullOrWhiteSpace(channel) ? title : channel;
                channel = "Canal do YouTube";
            }
            return new Track
            {
                Kind = forcedKind,
                Title = title,
                Channel = channel,
                Duration = FormatDuration(GetString(data, "duration")),
                Url = url,
                VideoId = id,
                Published = FirstNonEmpty(GetString(data, "release_date"), GetString(data, "upload_date"), GetString(data, "timestamp")),
                LikeStatus = ""
            };
        }

        private List<Track> SearchWithYtdlp(string query)
        {
            string output = RunYtdlp(GetYtdlpYoutubeArgs() + "--dump-json --flat-playlist --default-search ytsearch10 \"ytsearch10:" + EscapeArg(query) + "\"", 60000);
            var found = new List<Track>();
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var data = serializer.Deserialize<Dictionary<string, object>>(line);
                string url = GetString(data, "webpage_url");
                if (String.IsNullOrEmpty(url)) url = GetString(data, "url");
                if (!String.IsNullOrEmpty(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = "https://music.youtube.com/watch?v=" + url;
                found.Add(new Track { Title = GetString(data, "title", "Sem título"), Channel = GetString(data, "channel", GetString(data, "uploader", "")), Duration = FormatDuration(GetString(data, "duration")), Url = url, VideoId = GetString(data, "id"), LikeStatus = "" });
            }
            return found;
        }

        private void LoadBridgeList(string command, string status)
        {
            SetStatus(status);
            RunWorker(delegate { ReplaceList(TracksFromBridge(command), status.Replace("Carregando", "Carregado")); });
        }

        private void LoadRadio()
        {
            Track track = SelectedTrack();
            if (track == null) return;
            LoadRadioFromTrack(track);
        }

        private void LoadRadioFromTrack(Track track)
        {
            if (track == null) return;
            if (String.IsNullOrEmpty(track.VideoId)) { SetStatus("Este item não tem videoId para rádio."); return; }
            SetStatus("Carregando rádio.");
            RunWorker(delegate { ReplaceList(TracksFromBridge("watch", track.VideoId), "Rádio carregada."); });
        }

        private void LoadChannelVideos(Track track)
        {
            string url = TrackUrl(track);
            if (String.IsNullOrWhiteSpace(url)) { SetStatus("Canal sem link."); return; }
            SetStatus("Carregando vídeos recentes do canal " + track.Title + ".");
            RunWorker(delegate { ReplaceList(LoadChannelVideosWithYtdlp(url), "Vídeos recentes do canal carregados."); });
        }

        private List<Track> LoadChannelVideosWithYtdlp(string channelUrl)
        {
            string target = "\"" + EscapeArg(ChannelVideosUrl(channelUrl)) + "\"";
            string output = RunYtdlp(GetYtdlpYoutubeArgs() + "--dump-json --flat-playlist --playlist-end 30 " + target, 120000);
            var found = new List<Track>();
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var data = serializer.Deserialize<Dictionary<string, object>>(line);
                string id = GetString(data, "id");
                string url = GetString(data, "webpage_url");
                if (String.IsNullOrWhiteSpace(url)) url = GetString(data, "url");
                if (!String.IsNullOrWhiteSpace(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = "https://www.youtube.com/watch?v=" + url;
                if (String.IsNullOrWhiteSpace(url) && !String.IsNullOrWhiteSpace(id))
                    url = "https://www.youtube.com/watch?v=" + id;
                found.Add(new Track
                {
                    Kind = "track",
                    Title = GetString(data, "title", "Sem título"),
                    Channel = GetString(data, "channel", GetString(data, "uploader", "")),
                    Duration = FormatDuration(GetString(data, "duration")),
                    Url = url,
                    VideoId = id
                });
            }
            return found;
        }

        private string ChannelVideosUrl(string channelUrl)
        {
            string url = channelUrl.TrimEnd('/');
            if (url.EndsWith("/videos", StringComparison.OrdinalIgnoreCase) || url.Contains("/videos?")) return url;
            return url + "/videos";
        }

        private void LoadInfo(string command)
        {
            SetStatus("Carregando informações.");
            RunWorker(delegate
            {
                var data = RunBridge(command);
                BeginInvoke(new Action(delegate
                {
                    MessageBox.Show(FormatInfo(command, data), Text);
                    SetStatus("Informações carregadas.");
                }));
            });
        }

        private string FormatInfo(string command, Dictionary<string, object> data)
        {
            if (command == "account")
            {
                object accountObj;
                if (data.TryGetValue("account", out accountObj))
                {
                    var account = accountObj as Dictionary<string, object>;
                    if (account != null)
                    {
                        return "Conta logada\r\n\r\nNome: " + GetString(account, "accountName", "não informado")
                            + "\r\nCanal: " + GetString(account, "channelHandle", "não informado");
                    }
                }
            }
            return serializer.Serialize(data);
        }

        private void ShowLyrics()
        {
            Track track = SelectedTrack();
            if (track == null) return;
            if (String.IsNullOrEmpty(track.VideoId)) { SetStatus("Este item não tem videoId para letra."); return; }
            SetStatus("Carregando letra.");
            RunWorker(delegate
            {
                var data = RunBridge("lyrics", track.VideoId);
                string lyrics = GetString(data, "lyrics", "Letra não disponível.");
                BeginInvoke(new Action(delegate
                {
                    using (var form = new Form())
                    {
                        form.Text = "Letra";
                        form.Size = new Size(720, 520);
                        var box = new TextBox();
                        box.Multiline = true;
                        box.ReadOnly = true;
                        box.ScrollBars = ScrollBars.Both;
                        box.Dock = DockStyle.Fill;
                        box.Text = lyrics;
                        box.AccessibleName = "Texto da letra";
                        form.Controls.Add(box);
                        form.ShowDialog(this);
                    }
                    SetStatus("Letra carregada.");
                }));
            });
        }

        private void ReplaceList(List<Track> found, string status)
        {
            BeginInvoke(new Action(delegate
            {
                tracks.Clear();
                tracks.AddRange(found);
                localFolderAudioOnly = found.Count > 0 && found.All(t => !String.IsNullOrWhiteSpace(t.Url) && File.Exists(t.Url) && IsAudioFile(t.Url));
                if (!localFolderAudioOnly)
                {
                    localFolderPlaybackMode = "normal";
                    repeatOnceConsumed = false;
                }
                resultsList.Items.Clear();
                foreach (var track in tracks) resultsList.Items.Add(track);
                if (tracks.Count > 0)
                {
                    ShowResultsOnly();
                    resultsList.SelectedIndex = 0;
                    resultsList.Focus();
                    SetStatus(status + " " + tracks.Count + " itens.");
                }
                else
                {
                    ShowResultsOnly();
                    SetStatus("Nenhum item encontrado. Pressione Esc para voltar.");
                }
            }));
        }

        private List<Track> TracksFromBridge(string command, params string[] args)
        {
            var data = RunBridge(command, args);
            object itemsObj;
            var result = new List<Track>();
            if (!data.TryGetValue("items", out itemsObj)) return result;
            var items = itemsObj as IEnumerable;
            if (items == null) return result;
            foreach (object obj in items)
            {
                var item = obj as Dictionary<string, object>;
                if (item == null) continue;
                result.Add(new Track
                {
                    Kind = GetString(item, "kind", "track"),
                    Title = GetString(item, "title", "Sem título"),
                    Channel = GetString(item, "channel", ""),
                    Duration = GetString(item, "duration", ""),
                    Url = GetString(item, "url", ""),
                    VideoId = GetString(item, "videoId", ""),
                    BrowseId = GetString(item, "browseId", ""),
                    PlaylistId = GetString(item, "playlistId", ""),
                    LikeStatus = GetString(item, "likeStatus", "")
                });
            }
            return result;
        }

        private Dictionary<string, object> RunBridge(string command, params string[] args)
        {
            EnsurePythonRuntime(true);
            string bridge = Path.Combine(libraryDir, "ytmusic_bridge.py");
            string arguments = "\"" + EscapeArg(bridge) + "\" " + command;
            foreach (string arg in args) arguments += " \"" + EscapeArg(arg) + "\"";
            string output = RunProcess(GetPythonFileName(), arguments, 120000);
            string line = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (String.IsNullOrEmpty(line)) throw new Exception("ytmusic_bridge não retornou dados.");
            var data = serializer.Deserialize<Dictionary<string, object>>(line);
            object ok;
            if (data.TryGetValue("ok", out ok) && ok is bool && !(bool)ok)
                throw new Exception(GetString(data, "error", "Erro no ytmusic_bridge."));
            return data;
        }

        private void BrowserLogin()
        {
            string browserName;
            string browserCode;
            string browserExe;
            bool supported = GetDefaultBrowser(out browserName, out browserCode, out browserExe);
            AnnounceStatus("Abrindo seu navegador padrão, que é atualmente " + browserName + ".");
            if (!supported)
            {
                Process.Start(new ProcessStartInfo(GoogleLoginUrl()) { UseShellExecute = true });
                MessageBox.Show(
                    "Abri a página de contas do Google no seu navegador padrão, que é atualmente " + browserName + ".\r\n\r\nPor enquanto, para salvar o login automaticamente dentro do app, o navegador padrão precisa ser Google Chrome ou Microsoft Edge.",
                    Text);
                return;
            }
            if (String.IsNullOrEmpty(browserExe))
            {
                MessageBox.Show("Seu navegador padrão é " + browserName + ", mas não encontrei o executável dele neste computador.", Text);
                return;
            }
            StartSeparatedBrowserLogin(browserName, browserCode, browserExe);
        }

        private void StartSeparatedBrowserLogin(string browserName, string browserCode, string browserExe)
        {
            string profileDir = Path.Combine(configDir, "login_profile_" + browserCode);
            Directory.CreateDirectory(profileDir);
            int port = 9231;
            string browserArgs = "--remote-debugging-port=" + port
                + " --remote-allow-origins=http://127.0.0.1:" + port
                + " --user-data-dir=\"" + EscapeArg(profileDir) + "\""
                + " --no-first-run " + GoogleLoginUrl();
            Process.Start(new ProcessStartInfo(browserExe, browserArgs) { UseShellExecute = false });
            DialogResult answer = MessageBox.Show(
                "Abri uma janela separada no seu navegador padrão, " + browserName + ", com a página de contas do Google.\r\n\r\nPasso a passo:\r\n1. Entre na sua conta Google nessa janela.\r\n2. Quando o YouTube Music abrir logado, volte para este aplicativo.\r\n3. Clique OK para conectar a conta.\r\n\r\nNão precisa copiar nada e não precisa mexer em configurações técnicas.",
                Text,
                MessageBoxButtons.OKCancel);
            if (answer != DialogResult.OK) return;
            SetStatus("Salvando sessão da janela de login.");
            RunWorker(delegate
            {
                RunBridge("cdp_login", port.ToString());
                BeginInvoke(new Action(delegate
                {
                    UpdateLoginButtons();
                    SetStatus("Conta Google conectada.");
                })); 
            });
        }

        private void ShowTrackDescription(Track track)
        {
            if (!EnsureTrackForDetails(track)) return;
            SetStatus("Carregando descrição do vídeo.");
            RunWorker(delegate
            {
                try
                {
                    var data = LoadYtdlpVideoMetadata(track, false);
                    var text = new StringBuilder();
                    text.AppendLine(GetString(data, "title", track.Title));
                    string channel = GetString(data, "channel", GetString(data, "uploader", track.Channel));
                    if (!String.IsNullOrWhiteSpace(channel)) text.AppendLine("Canal: " + channel);
                    string duration = FormatDuration(GetString(data, "duration"));
                    if (!String.IsNullOrWhiteSpace(duration)) text.AppendLine("Duração: " + duration);
                    string published = FormatPublishedText(data);
                    if (!String.IsNullOrWhiteSpace(published)) text.AppendLine("Publicado: " + published);
                    text.AppendLine();
                    text.AppendLine(GetString(data, "description", "Descrição não disponível."));
                    BeginInvoke(new Action(delegate { ShowTextDialog("Descrição do vídeo", text.ToString()); SetStatus("Descrição carregada."); }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate { AnnounceStatus("Não consegui carregar a descrição. Detalhe: " + ShortError(ex.Message)); }));
                }
            });
        }

        private void ShowTrackComments(Track track)
        {
            if (!EnsureTrackForDetails(track)) return;
            SetStatus("Carregando comentários.");
            RunWorker(delegate
            {
                try
                {
                    var data = LoadYtdlpVideoMetadata(track, true);
                    object commentsObj;
                    var text = new StringBuilder();
                    if (data.TryGetValue("comments", out commentsObj))
                    {
                        var comments = commentsObj as IEnumerable;
                        int count = 0;
                        if (comments != null)
                        {
                            foreach (object obj in comments)
                            {
                                var comment = obj as Dictionary<string, object>;
                                if (comment == null) continue;
                                count++;
                                text.AppendLine(count + ". " + GetString(comment, "author", "Autor desconhecido"));
                                string time = GetString(comment, "time_text", "");
                                if (!String.IsNullOrWhiteSpace(time)) text.AppendLine(time);
                                text.AppendLine(GetString(comment, "text", ""));
                                text.AppendLine();
                                if (count >= 50) break;
                            }
                        }
                    }
                    if (text.Length == 0) text.AppendLine("Comentários não disponíveis para este vídeo.");
                    BeginInvoke(new Action(delegate { ShowTextDialog("Comentários", text.ToString()); SetStatus("Comentários carregados."); }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate { AnnounceStatus("Não consegui carregar comentários. Detalhe: " + ShortError(ex.Message)); }));
                }
            });
        }

        private void ShowTrackChapters(Track track)
        {
            if (!EnsureTrackForDetails(track)) return;
            SetStatus("Carregando capítulos.");
            RunWorker(delegate
            {
                try
                {
                    var chapters = LoadChapters(track);
                    BeginInvoke(new Action(delegate
                    {
                        if (chapters.Count == 0)
                        {
                            AnnounceStatus("Este vídeo não tem capítulos.");
                            return;
                        }
                        ShowChaptersDialog(chapters);
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate { AnnounceStatus("Não consegui carregar capítulos. Detalhe: " + ShortError(ex.Message)); }));
                }
            });
        }

        private void ShowTrackCaptions(Track track)
        {
            if (!EnsureTrackForDetails(track)) return;
            SetStatus("Carregando legendas.");
            RunWorker(delegate
            {
                string tempRoot = Path.Combine(Path.GetTempPath(), "Youtube_Light_Legendas_" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(tempRoot);
                    string template = Path.Combine(tempRoot, "legenda.%(ext)s");
                    string target = "\"" + EscapeArg(TrackUrl(track)) + "\"";
                    string args = GetYtdlpCookieArgs() + GetYtdlpYoutubeArgs() + "--skip-download --write-subs --write-auto-subs --sub-langs \"pt.*,en.*\" --sub-format vtt -o \"" + EscapeArg(template) + "\" " + target;
                    RunYtdlp(args, 120000);
                    string file = Directory.GetFiles(tempRoot, "*.vtt").FirstOrDefault();
                    string text = String.IsNullOrWhiteSpace(file) ? "" : CleanVtt(File.ReadAllText(file, Encoding.UTF8));
                    if (String.IsNullOrWhiteSpace(text)) text = "Legenda não disponível para este vídeo.";
                    BeginInvoke(new Action(delegate { ShowTextDialog("Legendas", text); SetStatus("Legendas carregadas."); }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate { AnnounceStatus("Não consegui carregar legendas. Detalhe: " + ShortError(ex.Message)); }));
                }
                finally
                {
                    try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
                }
            });
        }

        private void LoadRelatedVideos(Track track)
        {
            if (!EnsureTrackForDetails(track)) return;
            SetStatus("Carregando vídeos relacionados.");
            RunWorker(delegate
            {
                var found = new List<Track>();
                try
                {
                    var data = LoadYtdlpVideoMetadata(track, false);
                    object relatedObj;
                    if (data.TryGetValue("related_videos", out relatedObj))
                    {
                        var related = relatedObj as IEnumerable;
                        if (related != null)
                        {
                            foreach (object obj in related)
                            {
                                var item = obj as Dictionary<string, object>;
                                if (item == null) continue;
                                Track relatedTrack = TrackFromYtdlpDictionary(item, "track");
                                if (!String.IsNullOrWhiteSpace(relatedTrack.Title)) found.Add(relatedTrack);
                                if (found.Count >= 40) break;
                            }
                        }
                    }
                }
                catch { }
                if (found.Count == 0 && !String.IsNullOrWhiteSpace(track.VideoId))
                {
                    try { found = TracksFromBridge("watch", track.VideoId); } catch { }
                }
                ReplaceList(found, "Vídeos relacionados carregados.");
            });
        }

        private bool EnsureTrackForDetails(Track track)
        {
            if (track == null)
            {
                AnnounceStatus("Nenhum vídeo selecionado.");
                return false;
            }
            if (String.IsNullOrWhiteSpace(TrackUrl(track)))
            {
                AnnounceStatus("Este item não tem link de vídeo.");
                return false;
            }
            return true;
        }

        private Dictionary<string, object> LoadYtdlpVideoMetadata(Track track, bool includeComments)
        {
            string target = "\"" + EscapeArg(TrackUrl(track)) + "\"";
            string args = GetYtdlpCookieArgs() + GetYtdlpYoutubeArgs() + "--dump-single-json --no-playlist --no-warnings ";
            if (includeComments) args += "--write-comments ";
            string output = RunYtdlp(args + target, includeComments ? 180000 : 90000);
            return ParseJsonObjectFromOutput(output);
        }

        private Dictionary<string, object> ParseJsonObjectFromOutput(string output)
        {
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}")) continue;
                return serializer.Deserialize<Dictionary<string, object>>(trimmed);
            }
            throw new Exception("yt-dlp não retornou metadados em JSON.");
        }

        private void ShowTextDialog(string title, string text)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Size = new Size(760, 560);
                form.StartPosition = FormStartPosition.CenterParent;
                var box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.Dock = DockStyle.Fill;
                box.Text = text ?? "";
                box.AccessibleName = title;
                box.AccessibleDescription = "Texto carregado. Use as setas para ler.";
                form.Controls.Add(box);
                form.Shown += delegate { box.Focus(); box.SelectionStart = 0; box.SelectionLength = 0; };
                form.ShowDialog(this);
            }
        }

        private List<ChapterPoint> LoadChapters(Track track)
        {
            var data = LoadYtdlpVideoMetadata(track, false);
            object chaptersObj;
            var chapters = new List<ChapterPoint>();
            if (!data.TryGetValue("chapters", out chaptersObj)) return chapters;
            var chapterList = chaptersObj as IEnumerable;
            if (chapterList == null) return chapters;
            foreach (object obj in chapterList)
            {
                var item = obj as Dictionary<string, object>;
                if (item == null) continue;
                chapters.Add(new ChapterPoint
                {
                    Title = GetString(item, "title", "Capítulo"),
                    StartSeconds = GetDouble(item, "start_time", 0)
                });
            }
            return chapters;
        }

        private void ShowChaptersDialog(List<ChapterPoint> chapters)
        {
            using (var form = new Form())
            {
                form.Text = "Capítulos";
                form.Size = new Size(620, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Lista de capítulos";
                list.AccessibleDescription = "Use as setas. Pressione Enter para pular para o capítulo.";
                foreach (ChapterPoint chapter in chapters) list.Items.Add(chapter);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter || !(list.SelectedItem is ChapterPoint)) return;
                    e.SuppressKeyPress = true;
                    ChapterPoint chapter = (ChapterPoint)list.SelectedItem;
                    SeekToSeconds(chapter.StartSeconds);
                    AnnounceStatus("Pulando para " + chapter + ".");
                    form.Close();
                };
                form.Controls.Add(list);
                form.ShowDialog(this);
            }
        }

        private string CleanVtt(string raw)
        {
            var lines = new List<string>();
            string last = "";
            foreach (string line in (raw ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string text = Regex.Replace(line, "<[^>]+>", "").Trim();
                if (String.IsNullOrWhiteSpace(text)) continue;
                if (text.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Kind:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Language:", StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Contains("-->") || Regex.IsMatch(text, @"^\d+$")) continue;
                if (text == last) continue;
                lines.Add(text);
                last = text;
            }
            return String.Join("\r\n", lines.ToArray());
        }

        private string FormatPublishedText(Dictionary<string, object> data)
        {
            string relative = GetString(data, "availability", "");
            string uploadDate = GetString(data, "upload_date", "");
            string timestamp = GetString(data, "timestamp", "");
            if (!String.IsNullOrWhiteSpace(timestamp)) return HumanPublished(timestamp);
            if (String.IsNullOrWhiteSpace(uploadDate)) return relative;
            DateTime date;
            if (DateTime.TryParseExact(uploadDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date))
                return HumanPublished(date.ToString("yyyyMMdd"));
            return HumanPublished(uploadDate);
        }

        private string HumanPublished(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return "";
            long unix;
            DateTime date;
            if (Int64.TryParse(raw, out unix) && raw.Length <= 10)
                date = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            else if (!DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date))
                return raw;
            TimeSpan age = DateTime.Now - date;
            if (age.TotalMinutes < 2) return "há poucos minutos";
            if (age.TotalHours < 1) return "há " + Math.Max(1, (int)age.TotalMinutes) + " minutos";
            if (age.TotalDays < 1) return "há " + Math.Max(1, (int)age.TotalHours) + " horas";
            if (age.TotalDays < 31) return "há " + Math.Max(1, (int)age.TotalDays) + " dias";
            if (age.TotalDays < 365) return "há " + Math.Max(1, (int)(age.TotalDays / 30)) + " meses";
            return "há " + Math.Max(1, (int)(age.TotalDays / 365)) + " anos";
        }

        private string GoogleLoginUrl()
        {
            return "https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fmusic.youtube.com%2F";
        }

        private bool GetDefaultBrowser(out string browserName, out string browserCode, out string browserExe)
        {
            browserName = "navegador padrão";
            browserCode = "";
            browserExe = "";
            string progId = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ProgId");
                        if (value != null) progId = value.ToString();
                    }
                }
            }
            catch { }

            string lower = (progId ?? "").ToLowerInvariant();
            if (lower.Contains("microsoftedge") || lower.Contains("msedge"))
            {
                browserName = "Microsoft Edge";
                browserCode = "edge";
                browserExe = FindEdge();
                return true;
            }
            if (lower.Contains("chrome"))
            {
                browserName = "Google Chrome";
                browserCode = "chrome";
                browserExe = FindChrome();
                return true;
            }
            if (lower.Contains("firefox"))
            {
                browserName = "Mozilla Firefox";
                browserExe = FindBrowserFromAppPath("firefox.exe");
                return false;
            }
            if (lower.Contains("opera"))
            {
                browserName = "Opera";
                browserExe = FindBrowserFromAppPath("opera.exe");
                return false;
            }
            if (lower.Contains("brave"))
            {
                browserName = "Brave";
                browserExe = FindBrowserFromAppPath("brave.exe");
                return false;
            }

            string registryName = GetBrowserNameFromProgId(progId);
            if (!String.IsNullOrWhiteSpace(registryName)) browserName = registryName;
            return false;
        }

        private string GetBrowserNameFromProgId(string progId)
        {
            if (String.IsNullOrWhiteSpace(progId)) return "";
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(progId + @"\Application"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ApplicationName");
                        if (value != null && !String.IsNullOrWhiteSpace(value.ToString())) return value.ToString();
                    }
                }
            }
            catch { }
            return progId;
        }

        private string FindBrowserFromAppPath(string exeName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("");
                        if (value != null && File.Exists(value.ToString())) return value.ToString();
                    }
                }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("");
                        if (value != null && File.Exists(value.ToString())) return value.ToString();
                    }
                }
            }
            catch { }
            return RunWhere(exeName);
        }

        private void OpenIdeasEmail()
        {
            string browserName;
            string browserCode;
            string browserExe;
            GetDefaultBrowser(out browserName, out browserCode, out browserExe);
            string url = "https://mail.google.com/mail/?view=cm&fs=1&to=diegovinicius95891@gmail.com&su=" + Uri.EscapeDataString("Ideia para o Youtube Light");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            AnnounceStatus("Abrindo Gmail no seu navegador padrão, que é atualmente " + browserName + ".");
        }

        private void OpenDefaultAppsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
                AnnounceStatus("Abrindo configurações de aplicativos padrão do Windows. Escolha Youtube Light para os formatos de áudio e vídeo que quiser.");
            }
            catch (Exception ex)
            {
                AnnounceStatus("Não consegui abrir os aplicativos padrão do Windows. Detalhe: " + ShortError(ex.Message));
            }
        }

        private void Logout()
        {
            StopPlayback(false);
            DeleteIfExists(Path.Combine(configDir, "browser.json"));
            DeleteIfExists(Path.Combine(configDir, "cookies.txt"));
            DeleteIfExists(Path.Combine(configDir, "oauth.json"));
            DeleteIfExists(Path.Combine(configDir, "ytmusic_client.json"));
            streamCache.Clear();
            UpdateLoginButtons();
            SetStatus("Conta deslogada.");
        }

        private void DeleteIfExists(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { MessageBox.Show("Não consegui apagar " + path + "\r\n\r\n" + ex.Message, Text); }
        }

        private void MigrateLegacyLayout()
        {
            try
            {
                MigrateLegacyLibrary();
                CopyLegacyFile("browser.json", configDir);
                CopyLegacyFile("cookies.txt", configDir);
                CopyLegacyFile("oauth.json", configDir);
                CopyLegacyFile("ytmusic_client.json", configDir);
                CopyLegacyFile("player_config.txt", configDir);
                CopyLegacyFile("mpv_install_attempted.flag", configDir);
                CopyLegacyFile("vlc_install_attempted.flag", configDir);
                CopyLegacyFile("node_install_attempted.flag", configDir);
                CopyLegacyFile("pending_update_notes.txt", configDir);
                CopyLegacyDirectory("downloads", configDir);
                CopyLegacyDirectory("login_profile", configDir);
                CopyLegacyDirectory("login_profile_chrome", configDir);
                CopyLegacyDirectory("login_profile_google chrome", configDir);
                CopyLegacyFile("nvdaControllerClient64.dll", libraryDir);
                CopyLegacyFile("ytmusic_bridge.py", libraryDir);
                MigrateConfigFile("player_config.txt", configFile);
                MigrateConfigFile("pending_update_notes.txt", pendingUpdateNotesFile);
                MigrateConfigFile("videos_notificados.json", notifiedVideosFile);
                MigrateConfigFile("favoritos_locais.json", LocalDataFile("favoritos_locais"));
                MigrateConfigFile("historico_local.json", LocalDataFile("historico_local"));
                MigrateConfigFile("fila_reproducao.json", LocalDataFile("fila_reproducao"));
            }
            catch { }
        }

        private void MigrateLegacyLibrary()
        {
            try
            {
                if (!Directory.Exists(legacyLibraryDir)) return;
                Directory.CreateDirectory(libraryDir);
                foreach (string file in new[] { "nvdaControllerClient64.dll", "ytmusic_bridge.py", "vlc_player.py", "mic_monitor.py" })
                {
                    string source = Path.Combine(legacyLibraryDir, file);
                    string target = Path.Combine(libraryDir, file);
                    if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target, true);
                }
                string oldRuntime = Path.Combine(legacyLibraryDir, "Runtime");
                if (Directory.Exists(oldRuntime) && !Directory.Exists(runtimeDir))
                    Directory.Move(oldRuntime, runtimeDir);
            }
            catch { }
        }

        private void MigrateConfigFile(string oldName, string newPath)
        {
            try
            {
                string oldPath = Path.Combine(configDir, oldName);
                if (!File.Exists(oldPath) || File.Exists(newPath)) return;
                File.Move(oldPath, newPath);
            }
            catch { }
        }

        private void CopyLegacyFile(string name, string targetDir)
        {
            string source = FindLegacyFile(name);
            if (!File.Exists(source)) return;
            Directory.CreateDirectory(targetDir);
            string target = Path.Combine(targetDir, name);
            if (!File.Exists(target)) File.Copy(source, target, false);
        }

        private void CopyLegacyDirectory(string name, string targetParent)
        {
            string source = FindLegacyDirectory(name);
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(targetParent);
            string target = Path.Combine(targetParent, name);
            CopyDirectoryWithoutOverwrite(source, target);
        }

        private string FindLegacyFile(string name)
        {
            string inLegacyConfig = Path.Combine(legacyConfigDir, name);
            if (File.Exists(inLegacyConfig)) return inLegacyConfig;
            return Path.Combine(baseDir, name);
        }

        private string FindLegacyDirectory(string name)
        {
            string inLegacyConfig = Path.Combine(legacyConfigDir, name);
            if (Directory.Exists(inLegacyConfig)) return inLegacyConfig;
            return Path.Combine(baseDir, name);
        }

        private void CopyDirectoryWithoutOverwrite(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in Directory.GetDirectories(source))
            {
                string targetDirectory = Path.Combine(target, Path.GetFileName(directory));
                CopyDirectoryWithoutOverwrite(directory, targetDirectory);
            }
            foreach (string file in Directory.GetFiles(source))
            {
                string targetFile = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(targetFile)) File.Copy(file, targetFile, false);
            }
        }

        private void DeleteLegacyRootFile(string name)
        {
            DeleteIfInsideBase(Path.Combine(baseDir, name));
        }

        private void DeleteLegacyDirectory(string name)
        {
            DeleteIfInsideBase(Path.Combine(baseDir, name));
        }

        private void DeleteIfInsideBase(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return;
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(baseDir + Path.DirectorySeparatorChar);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
                if (File.Exists(full)) File.Delete(full);
                else Directory.Delete(full, true);
            }
            catch { }
        }

        private bool IsLoggedIn()
        {
            return File.Exists(Path.Combine(configDir, "browser.json")) || File.Exists(Path.Combine(configDir, "oauth.json")) || File.Exists(Path.Combine(configDir, "cookies.txt"));
        }

        private void UpdateLoginButtons()
        {
            PopulateMoreList();
        }

        private void PlaySelected()
        {
            Track track = SelectedTrack();
            if (track == null) return;
            if (track.Kind == "channel")
            {
                string url = TrackUrl(track);
                if (String.IsNullOrWhiteSpace(url)) { SetStatus("Canal sem link."); return; }
                LoadChannelVideos(track);
                return;
            }
            if (track.Kind == "playlist")
            {
                if (String.IsNullOrEmpty(track.PlaylistId) && !String.IsNullOrWhiteSpace(track.Url))
                {
                    LoadYoutubePlaylist(track);
                    return;
                }
                if (String.IsNullOrEmpty(track.PlaylistId)) { SetStatus("Playlist sem ID."); return; }
                SetStatus("Abrindo playlist.");
                RunWorker(delegate { ReplaceList(TracksFromBridge("playlist", track.PlaylistId), "Playlist aberta."); });
                return;
            }
            currentIndex = resultsList.SelectedIndex;
            PlayTrack(track);
        }

        private void LoadYoutubePlaylist(Track track)
        {
            string url = TrackUrl(track);
            if (String.IsNullOrWhiteSpace(url)) { SetStatus("Playlist sem link."); return; }
            SetStatus("Abrindo playlist " + track.Title + ".");
            RunWorker(delegate { ReplaceList(LoadPlaylistWithYtdlp(url), "Playlist carregada."); });
        }

        private List<Track> LoadPlaylistWithYtdlp(string playlistUrl)
        {
            string target = "\"" + EscapeArg(playlistUrl) + "\"";
            string output = RunYtdlp(GetYtdlpYoutubeArgs() + "--dump-json --flat-playlist " + target, 120000);
            return TracksFromYtdlpJsonLines(output, "track");
        }

        private void PlayTrack(Track track)
        {
            string url = track.Url;
            if (String.IsNullOrEmpty(url) && !String.IsNullOrEmpty(track.VideoId))
                url = "https://music.youtube.com/watch?v=" + track.VideoId;
            if (String.IsNullOrEmpty(url)) { SetStatus("Este item não tem URL tocável."); return; }

            if (File.Exists(url))
            {
                PlayLocalMediaTrack(track, url);
                return;
            }

            AddToLocalHistory(track);
            SetProgress(true);
            AnnounceStatus("Preparando reprodução de " + track.Title + ".");
            SetStatus("Preparando reprodução de " + track.Title + ".");
            RunWorker(delegate
            {
                string mpvEx = "";
                string ffplayEx = "";
                string vlcEx = "";
                string internalEx = "";
                try
                {
                    EnsureMpvAvailable();
                    string mediaPath = preferTemporaryAudio ? ResolveLocalAudioPath(url, track.VideoId) : ResolveStreamUrl(url);
                    Invoke(new Action(delegate { StopPlayback(false); }));
                    StartMpvPlayback(mediaPath);
                    BeginInvoke(new Action(delegate
                    {
                        currentTempAudioPath = "";
                        MarkPlaybackStarted(track, url);
                        PrefetchNext();
                    }));
                    return;
                }
                catch (Exception ex)
                {
                    mpvEx = ShortError(ex.Message);
                }

                try
                {
                    EnsureFfplayAvailable();
                    string mediaPath = preferTemporaryAudio ? ResolveLocalAudioPath(url, track.VideoId) : ResolveStreamUrl(url);
                    Invoke(new Action(delegate { StopPlayback(false); }));
                    StartFfplayPlayback(mediaPath);
                    BeginInvoke(new Action(delegate
                    {
                        currentTempAudioPath = "";
                        MarkPlaybackStarted(track, url);
                        PrefetchNext();
                    }));
                    return;
                }
                catch (Exception ex)
                {
                    ffplayEx = ShortError(ex.Message);
                }

                try
                {
                    EnsureVlcAvailable();
                    string mediaPath = preferTemporaryAudio ? ResolveLocalAudioPath(url, track.VideoId) : ResolveStreamUrl(url);
                    Invoke(new Action(delegate { StopPlayback(false); }));
                    StartVlcPlayback(mediaPath);
                    BeginInvoke(new Action(delegate
                    {
                        currentTempAudioPath = "";
                        MarkPlaybackStarted(track, url);
                        PrefetchNext();
                    }));
                    return;
                }
                catch (Exception ex)
                {
                    vlcEx = ShortError(ex.Message);
                }

                try
                {
                    string audioPath = ResolveLocalAudioPath(url, track.VideoId);
                    BeginInvoke(new Action(delegate
                    {
                        StopPlayback(false);
                        if (preferTemporaryAudio)
                        {
                            try
                            {
                                StartMpvPlayback(audioPath);
                                currentTempAudioPath = IsTemporaryAudioFile(audioPath) ? audioPath : "";
                                MarkPlaybackStarted(track, url);
                                PrefetchNext();
                                return;
                            }
                            catch (Exception ex)
                            {
                                internalEx = ShortError(ex.Message);
                                StopMpv();
                            }
                        }
                        if (!EnsureInternalPlayer())
                        {
                            SetProgress(false);
                            AnnounceStatus("Não consegui tocar esta música. Detalhe: " + mpvEx + " / " + vlcEx + " / " + internalEx + " / Windows Media Player interno não disponível.");
                            return;
                        }
                        SetComProperty(internalPlayer, "URL", audioPath);
                        ApplySavedVolume();
                        CallComMethod(GetComProperty(internalPlayer, "controls"), "play");
                        usingVlc = false;
                        usingMpv = false;
                        currentTempAudioPath = IsTemporaryAudioFile(audioPath) ? audioPath : "";
                        MarkPlaybackStarted(track, url);
                        PrefetchNext();
                    }));
                }
                catch (Exception fallbackEx)
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Não consegui tocar esta música. Detalhe: " + ffplayEx + " / " + mpvEx + " / " + vlcEx + " / " + ShortError(fallbackEx.Message));
                    }));
                }
            }, true);
        }

        private void TryStartInternalPlayback(Track track, string sourceUrl, string audioPath)
        {
            try
            {
                StopPlayback(false);
                if (!EnsureInternalPlayer())
                {
                    SetProgress(false);
                    AnnounceStatus("Não consegui iniciar o player interno do Windows.");
                    return;
                }
                SetComProperty(internalPlayer, "URL", audioPath);
                ApplySavedVolume();
                CallComMethod(GetComProperty(internalPlayer, "controls"), "play");
                usingVlc = false;
                usingMpv = false;
                currentTempAudioPath = IsTemporaryAudioFile(audioPath) ? audioPath : "";
                MarkPlaybackStarted(track, sourceUrl);
                PrefetchNext();
            }
            catch (Exception ex)
            {
                SetProgress(false);
                AnnounceStatus("Não consegui tocar no player interno. Detalhe: " + ShortError(ex.Message));
            }
        }

        private void PlayLocalMediaTrack(Track track, string path)
        {
            AddToLocalHistory(track);
            SetProgress(true);
            SetStatus("Preparando arquivo local " + track.Title + ".");
            RunWorker(delegate
            {
                try
                {
                    EnsureFfplayAvailable();
                    Invoke(new Action(delegate { StopPlayback(false); }));
                    StartFfplayPlayback(path);
                    BeginInvoke(new Action(delegate
                    {
                        currentTempAudioPath = "";
                        MarkPlaybackStarted(track, path);
                        PrefetchNext();
                    }));
                }
                catch (Exception ffplayEx)
                {
                    try
                    {
                        EnsureMpvAvailable();
                        Invoke(new Action(delegate { StopPlayback(false); }));
                        StartMpvPlayback(path);
                        BeginInvoke(new Action(delegate
                        {
                            currentTempAudioPath = "";
                            MarkPlaybackStarted(track, path);
                            PrefetchNext();
                        }));
                    }
                    catch (Exception mpvEx)
                    {
                        try
                        {
                            EnsureVlcAvailable();
                            Invoke(new Action(delegate { StopPlayback(false); }));
                            StartVlcPlayback(path);
                            BeginInvoke(new Action(delegate
                            {
                                currentTempAudioPath = "";
                                MarkPlaybackStarted(track, path);
                                PrefetchNext();
                            }));
                        }
                        catch (Exception vlcEx)
                        {
                            try
                            {
                                BeginInvoke(new Action(delegate
                                {
                                    StopPlayback(false);
                                    if (!EnsureInternalPlayer())
                                    {
                                        SetProgress(false);
                                        AnnounceStatus("Não consegui tocar o arquivo local. Detalhe: " + ShortError(ffplayEx.Message) + " / " + ShortError(mpvEx.Message) + " / " + ShortError(vlcEx.Message) + " / Windows Media Player interno não disponível.");
                                        return;
                                    }
                                    SetComProperty(internalPlayer, "URL", path);
                                    ApplySavedVolume();
                                    CallComMethod(GetComProperty(internalPlayer, "controls"), "play");
                                    usingVlc = false;
                                    usingMpv = false;
                                    usingFfplay = false;
                                    currentTempAudioPath = "";
                                    MarkPlaybackStarted(track, path);
                                    PrefetchNext();
                                }));
                            }
                            catch (Exception ex)
                            {
                                BeginInvoke(new Action(delegate
                                {
                                    SetProgress(false);
                                    AnnounceStatus("Não consegui tocar o arquivo local. Detalhe: " + ShortError(ffplayEx.Message) + " / " + ShortError(mpvEx.Message) + " / " + ShortError(vlcEx.Message) + " / " + ShortError(ex.Message));
                                }));
                            }
                        }
                    }
                }
            }, true);
        }

        private void MarkPlaybackStarted(Track track, string url)
        {
            suppressAutoAdvance = false;
            playbackStarted = true;
            playbackPaused = false;
            playbackStartedAt = DateTime.Now;
            currentTrack = CloneTrack(track);
            currentTrackTitle = track.Title;
            currentTrackUrl = url;
            currentVideoId = track.VideoId;
            currentTrackDuration = track.Duration;
            UpdateWindowTitle();
            SetPlayerText("Tocando: " + track.Title);
            ShowPlayerSection();
            SetProgress(false);
            AnnounceStatus("Reproduzindo vídeo " + track.Title + ".");
            if (playerList != null) playerList.Focus();
        }

        private string ResolvePlayableAudioPath(string url, string videoId)
        {
            return ResolveStreamUrl(url);
        }

        private string ResolvePlayableAudioPath(string url, string videoId, bool allowStreamUrl)
        {
            return allowStreamUrl ? ResolveStreamUrl(url) : ResolveLocalAudioPath(url, videoId);
        }

        private string ResolveStreamUrl(string url)
        {
            string cached;
            if (streamCache.TryGetValue(url, out cached) && IsUsableMediaSource(cached) && cached.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return cached;
            string lastError = "";
            foreach (string args in BuildPlaybackStreamAttempts(url))
            {
                try
                {
                    string output = RunYtdlp(args, 90000);
                    string directUrl = ExtractDirectMediaUrl(output);
                    if (!String.IsNullOrWhiteSpace(directUrl))
                    {
                        streamCache[url] = directUrl;
                        return directUrl;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ShortError(ex.Message);
                }
            }
            throw new Exception("Não consegui obter stream direto. Último detalhe: " + lastError);
        }

        private string ResolveLocalAudioPath(string url, string videoId)
        {
            string cached;
            if (streamCache.TryGetValue(url, out cached) && !String.IsNullOrWhiteSpace(cached) && File.Exists(cached)) return cached;
            string key = SafeFileName(String.IsNullOrWhiteSpace(videoId) ? Math.Abs(url.GetHashCode()).ToString(System.Globalization.CultureInfo.InvariantCulture) : videoId);
            string template = Path.Combine(tempAudioDir, key + ".%(ext)s");
            string lastError = "";
            foreach (string args in BuildPlaybackYtdlpAttempts(template, url))
            {
                try
                {
                    DeletePartialAudioFiles(key);
                    RunYtdlp(args, 150000);
                    string found = Directory.GetFiles(tempAudioDir, key + ".*")
                        .Where(p => !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTime)
                        .FirstOrDefault();
                    if (!String.IsNullOrWhiteSpace(found) && File.Exists(found))
                    {
                        streamCache[url] = found;
                        return found;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ShortError(ex.Message);
                }
            }
            throw new Exception("Não consegui preparar o áudio local depois de tentar formatos com login, sem login e formatos alternativos. Último detalhe: " + lastError);
        }

        private bool IsUsableMediaSource(string source)
        {
            if (String.IsNullOrWhiteSpace(source)) return false;
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
            return File.Exists(source);
        }

        private string ExtractDirectMediaUrl(string output)
        {
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return trimmed;
            }
            return "";
        }

        private List<string> BuildPlaybackStreamAttempts(string url)
        {
            string cookieArgs = GetYtdlpCookieArgs();
            string defaultClients = GetYtdlpYoutubeArgsForClients("android,web,mweb,ios");
            string fallbackClients = GetYtdlpYoutubeArgsForClients("android,web,mweb,ios,tv");
            string audioFormat = "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best";
            string broadFormat = "ba/b/best";
            string target = "\"" + EscapeArg(url) + "\"";
            var attempts = new List<string>();
            attempts.Add(defaultClients + "-f \"" + audioFormat + "\" -g " + target);
            attempts.Add(fallbackClients + "-f \"" + broadFormat + "\" -g " + target);
            attempts.Add(cookieArgs + defaultClients + "-f \"" + audioFormat + "\" -g " + target);
            attempts.Add(cookieArgs + fallbackClients + "-f \"" + broadFormat + "\" -g " + target);
            attempts.Add("--no-warnings -f best -g " + target);
            return attempts;
        }

        private List<string> BuildPlaybackYtdlpAttempts(string template, string url)
        {
            string cookieArgs = GetYtdlpCookieArgs();
            string noCookieArgs = "";
            string defaultClients = GetYtdlpYoutubeArgsForClients("android,web,mweb,ios");
            string fallbackClients = GetYtdlpYoutubeArgsForClients("android,web,mweb,ios,tv");
            string audioFormat = "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best";
            string broadFormat = "ba/b/best";
            string baseArgs = "--no-playlist --no-part --force-overwrites -x --audio-format mp3 --audio-quality 5 -o \"" + EscapeArg(template) + "\" ";
            string target = "\"" + EscapeArg(url) + "\"";
            var attempts = new List<string>();
            attempts.Add(noCookieArgs + defaultClients + "-f \"" + audioFormat + "\" " + baseArgs + target);
            attempts.Add(noCookieArgs + fallbackClients + "-f \"" + broadFormat + "\" " + baseArgs + target);
            attempts.Add(cookieArgs + defaultClients + "-f \"" + audioFormat + "\" " + baseArgs + target);
            attempts.Add(cookieArgs + fallbackClients + "-f \"" + broadFormat + "\" " + baseArgs + target);
            attempts.Add(noCookieArgs + "--no-warnings -f best " + baseArgs + target);
            return attempts;
        }

        private void DeletePartialAudioFiles(string key)
        {
            try
            {
                foreach (string file in Directory.GetFiles(tempAudioDir, key + ".*"))
                {
                    string full = Path.GetFullPath(file);
                    string root = Path.GetFullPath(tempAudioDir + Path.DirectorySeparatorChar);
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) File.Delete(full);
                }
            }
            catch { }
        }

        private bool IsTemporaryAudioFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(tempAudioDir + Path.DirectorySeparatorChar);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void DeleteFinishedTemporaryAudio()
        {
            try
            {
                string path = currentTempAudioPath;
                currentTempAudioPath = "";
                if (IsTemporaryAudioFile(path)) File.Delete(path);
                ClearOldTemporaryAudioFiles();
            }
            catch { }
        }

        private void ClearOldTemporaryAudioFiles()
        {
            try
            {
                if (!Directory.Exists(tempAudioDir)) return;
                DateTime limit = DateTime.Now.AddMinutes(-20);
                foreach (string file in Directory.GetFiles(tempAudioDir))
                {
                    if (String.Equals(file, currentTempAudioPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.GetLastWriteTime(file) < limit) File.Delete(file);
                }
            }
            catch { }
        }

        private void EnsureVlcAvailable()
        {
            EnsurePythonRuntime(false);
            if (CanStartPythonVlc()) return;
            if (PortableRuntimeLooksComplete()) throw new Exception("Runtime portátil encontrado, mas o VLC portátil não iniciou.");
            throw new Exception("VLC portátil não está disponível.");
        }

        private bool CanStartPythonVlc()
        {
            try
            {
                if (!File.Exists(Path.Combine(runtimeDir, "Python", "python.exe"))) return false;
                string vlcDir = FindVlcDirectory();
                var psi = new ProcessStartInfo(GetPythonFileName(), "-c \"import vlc; i=vlc.Instance('--no-video','--quiet'); print('ok' if i else 'fail')\"");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(vlcDir)) prefix = vlcDir + ";" + prefix;
                if (!String.IsNullOrWhiteSpace(prefix)) psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
                using (var process = Process.Start(psi))
                {
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Contains("ok");
                }
            }
            catch { return false; }
        }

        private string FindVlc()
        {
            string portable = GetPortableTool(Path.Combine("VLC", "vlc.exe"));
            if (!String.IsNullOrEmpty(portable)) return portable;
            string found = RunWhere("vlc.exe");
            if (!String.IsNullOrEmpty(found)) return found;
            string[] candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe")
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return "";
        }

        private string FindVlcDirectory()
        {
            string vlc = FindVlc();
            return String.IsNullOrEmpty(vlc) ? "" : Path.GetDirectoryName(vlc);
        }

        private void StartVlcPlayback(string mediaPath)
        {
            string helper = Path.Combine(libraryDir, "vlc_player.py");
            if (!File.Exists(helper)) throw new Exception("Arquivo do player VLC não encontrado.");
            string args = "\"" + EscapeArg(helper) + "\" \"" + EscapeArg(mediaPath) + "\" " + savedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var psi = new ProcessStartInfo(GetPythonFileName(), args);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                psi.EnvironmentVariables["YOUTUBE_LIGHT_CONFIG_DIR"] = configDir;
                psi.EnvironmentVariables["YOUTUBE_LIGHT_LIBRARY_DIR"] = libraryDir;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string vlcDir = FindVlcDirectory();
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrEmpty(vlcDir)) prefix = vlcDir + ";" + prefix;
                if (!String.IsNullOrWhiteSpace(prefix)) psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
            }
            catch { }
            vlcProcess = Process.Start(psi);
            vlcInput = vlcProcess.StandardInput;
            usingVlc = true;
            string ready = ReadVlcLine(7000);
            if (String.IsNullOrWhiteSpace(ready) || !ready.Contains("\"ok\": true"))
            {
                string detail = ExtractJsonError(ready);
                try { if (vlcProcess != null && vlcProcess.HasExited) detail = FirstNonEmpty(detail, vlcProcess.StandardError.ReadToEnd().Trim()); } catch { }
                StopVlc();
                throw new Exception("O VLC não confirmou o início da reprodução" + (String.IsNullOrWhiteSpace(detail) ? "." : ": " + detail));
            }
            currentMediaPath = mediaPath;
            ApplySelectedOutputDeviceToVlc();
            StartPlayerMonitorIfNeeded(mediaPath);
        }

        private void EnsureFfplayAvailable()
        {
            if (!File.Exists(GetFfplayPath())) throw new Exception("ffplay.exe não encontrado na pasta do FFmpeg.");
        }

        private string GetFfplayPath()
        {
            string portable = GetPortableTool(Path.Combine("FFmpeg", "bin", "ffplay.exe"));
            if (!String.IsNullOrEmpty(portable)) return portable;
            string found = RunWhere("ffplay.exe");
            return String.IsNullOrEmpty(found) ? "ffplay.exe" : found;
        }

        private void StartFfplayPlayback(string mediaPath)
        {
            string fileName = GetFfplayPath();
            var psi = new ProcessStartInfo(fileName, "-nodisp -autoexit -hide_banner -loglevel error " + "\"" + EscapeArg(mediaPath) + "\"");
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            ffplayProcess = Process.Start(psi);
            usingFfplay = true;
            usingMpv = false;
            usingVlc = false;
            currentMediaPath = mediaPath;
            try { ffplayProcess.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { }; ffplayProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { }; } catch { }
            try { ffplayProcess.BeginOutputReadLine(); } catch { }
            try { ffplayProcess.BeginErrorReadLine(); } catch { }
            System.Threading.Thread.Sleep(1200);
            if (ffplayProcess == null || ffplayProcess.HasExited)
            {
                string detail = "";
                try { if (ffplayProcess != null) detail = ffplayProcess.StandardError.ReadToEnd().Trim(); } catch { }
                StopFfplay();
                throw new Exception("O ffplay não confirmou o início da reprodução" + (String.IsNullOrWhiteSpace(detail) ? "." : ": " + detail));
            }
        }

        private void StopFfplay()
        {
            try
            {
                if (ffplayProcess != null && !ffplayProcess.HasExited)
                    ffplayProcess.Kill();
            }
            catch { }
            ffplayProcess = null;
            usingFfplay = false;
            currentMediaPath = "";
        }

        private void StopVlc()
        {
            try { SendVlcCommand("{\"command\":\"stop\"}", false); } catch { }
            StopPlayerMonitor();
            try
            {
                if (vlcProcess != null && !vlcProcess.HasExited)
                    vlcProcess.Kill();
            }
            catch { }
            vlcProcess = null;
            vlcInput = null;
            usingVlc = false;
            currentMediaPath = "";
        }

        private void SendVlcCommand(string json, bool waitReply)
        {
            lock (vlcLock)
            {
                if (vlcProcess == null || vlcProcess.HasExited || vlcInput == null) throw new Exception("VLC não está aberto.");
                vlcInput.WriteLine(json);
                vlcInput.Flush();
                if (waitReply) ReadVlcLine(1500);
            }
        }

        private string QueryVlc(string json)
        {
            lock (vlcLock)
            {
                if (vlcProcess == null || vlcProcess.HasExited || vlcInput == null) throw new Exception("VLC não está aberto.");
                vlcInput.WriteLine(json);
                vlcInput.Flush();
                return ReadVlcLine(1500);
            }
        }

        private string ReadVlcLine(int timeoutMs)
        {
            if (vlcProcess == null) return "";
            var task = Task.Factory.StartNew(delegate { return vlcProcess.StandardOutput.ReadLine(); });
            return task.Wait(timeoutMs) ? (task.Result ?? "") : "";
        }

        private void StartPlayerMonitorIfNeeded(string mediaPath)
        {
            if (!playerMonitorEnabled || String.IsNullOrWhiteSpace(selectedMonitorOutputDeviceId)) return;
            StopPlayerMonitor();
            try
            {
                string helper = Path.Combine(libraryDir, usingMpv ? "mpv_player.py" : "vlc_player.py");
                if (!File.Exists(helper)) return;
                string monitorMediaArg = usingMpv ? WriteMpvMediaArgument(mediaPath) : mediaPath;
                string args = "\"" + EscapeArg(helper) + "\" \"" + EscapeArg(monitorMediaArg) + "\" " + playerMonitorVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var psi = new ProcessStartInfo(GetPythonFileName(), args);
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                try
                {
                    psi.EnvironmentVariables["YOUTUBE_LIGHT_CONFIG_DIR"] = configDir;
                    psi.EnvironmentVariables["YOUTUBE_LIGHT_LIBRARY_DIR"] = libraryDir;
                    string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                    string prefix = GetRuntimePathPrefix();
                    if (!String.IsNullOrWhiteSpace(prefix)) psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
                }
                catch { }
                playerMonitorProcess = Process.Start(psi);
                playerMonitorInput = playerMonitorProcess.StandardInput;
                string ready = ReadPlayerMonitorLine(5000);
                if (String.IsNullOrWhiteSpace(ready) || !ready.Contains("\"ok\": true"))
                {
                    StopPlayerMonitor();
                    return;
                }
                SendPlayerMonitorCommand("{\"command\":\"set-device\",\"id\":\"" + JsonEscape(selectedMonitorOutputDeviceId) + "\"}", true);
            }
            catch { StopPlayerMonitor(); }
        }

        private void RestartPlayerMonitor()
        {
            if ((!usingVlc && !usingMpv) || usingFfplay || String.IsNullOrWhiteSpace(currentMediaPath)) return;
            StartPlayerMonitorIfNeeded(currentMediaPath);
        }

        private void StopPlayerMonitor()
        {
            try { SendPlayerMonitorCommand("{\"command\":\"stop\"}", false); } catch { }
            try
            {
                if (playerMonitorProcess != null && !playerMonitorProcess.HasExited)
                    playerMonitorProcess.Kill();
            }
            catch { }
            playerMonitorProcess = null;
            playerMonitorInput = null;
        }

        private string ReadPlayerMonitorLine(int timeoutMs)
        {
            if (playerMonitorProcess == null) return "";
            var task = Task.Factory.StartNew(delegate { return playerMonitorProcess.StandardOutput.ReadLine(); });
            return task.Wait(timeoutMs) ? (task.Result ?? "") : "";
        }

        private void SendPlayerMonitorCommand(string json, bool waitReply)
        {
            lock (playerMonitorLock)
            {
                if (playerMonitorProcess == null || playerMonitorProcess.HasExited || playerMonitorInput == null) return;
                playerMonitorInput.WriteLine(json);
                playerMonitorInput.Flush();
                if (waitReply) ReadPlayerMonitorLine(1000);
            }
        }

        private double GetVlcNumber(string command, string key)
        {
            string json = QueryVlc("{\"command\":\"" + command + "\"}");
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            object value;
            if (data != null && data.TryGetValue(key, out value))
                return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return 0;
        }

        private void ChooseOutputDevice()
        {
            if (!usingVlc && !usingMpv)
            {
                AnnounceStatus("Para listar alto-falantes e fones, toque um vídeo primeiro.");
                return;
            }
            List<AudioDevice> devices = GetActiveAudioDevices();
            if (devices.Count == 0)
            {
                AnnounceStatus("Não encontrei dispositivos de saída no player.");
                return;
            }
            using (var form = new Form())
            {
                form.Text = "Trocar saída principal";
                form.Size = new Size(620, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Dispositivos de saída principal ou transmissão";
                list.AccessibleDescription = "Use as setas para escolher e pressione Enter.";
                foreach (AudioDevice device in devices) list.Items.Add(device);
                int selectedIndex = devices.FindIndex(d => String.Equals(d.Id, selectedOutputDeviceId, StringComparison.OrdinalIgnoreCase));
                list.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Usar este dispositivo", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK || !(list.SelectedItem is AudioDevice)) return;
                AudioDevice selected = (AudioDevice)list.SelectedItem;
                if (usingVlc) SendVlcCommand("{\"command\":\"set-device\",\"id\":\"" + JsonEscape(selected.Id) + "\"}", true);
                else if (usingMpv) SendMpvCommand("{\"command\":\"set-device\",\"id\":\"" + JsonEscape(selected.Id) + "\"}");
                selectedOutputDeviceId = selected.Id;
                selectedOutputDeviceName = selected.ToString();
                SaveConfig();
                ApplySelectedOutputDeviceToMicMonitor();
                AnnounceStatus("Saída de áudio alterada para " + selected + ".");
            }
        }

        private void ApplySelectedOutputDeviceToVlc()
        {
            if (!usingVlc || String.IsNullOrWhiteSpace(selectedOutputDeviceId)) return;
            try { SendVlcCommand("{\"command\":\"set-device\",\"id\":\"" + JsonEscape(selectedOutputDeviceId) + "\"}", true); }
            catch { }
        }

        private void ApplySelectedOutputDeviceToMpv()
        {
            if (!usingMpv || String.IsNullOrWhiteSpace(selectedOutputDeviceId)) return;
            try { SendMpvCommand("{\"command\":\"set-device\",\"id\":\"" + JsonEscape(selectedOutputDeviceId) + "\"}"); }
            catch { }
        }

        private List<AudioDevice> GetActiveAudioDevices()
        {
            if (usingMpv) return GetMpvAudioDevices();
            return GetVlcAudioDevices();
        }

        private List<AudioDevice> GetVlcAudioDevices()
        {
            var result = new List<AudioDevice>();
            string json = QueryVlc("{\"command\":\"list-devices\"}");
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            object devicesObj;
            if (data == null || !data.TryGetValue("devices", out devicesObj)) return result;
            var items = devicesObj as IEnumerable;
            if (items == null) return result;
            foreach (object obj in items)
            {
                var item = obj as Dictionary<string, object>;
                if (item == null) continue;
                string id = GetString(item, "id", "");
                string name = GetString(item, "name", id);
                if (!String.IsNullOrWhiteSpace(id) || !String.IsNullOrWhiteSpace(name))
                    result.Add(new AudioDevice { Id = id, Name = name });
            }
            return result;
        }

        private List<AudioDevice> GetMpvAudioDevices()
        {
            var result = new List<AudioDevice>();
            string json = QueryMpv("{\"command\":\"list-devices\"}");
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            object devicesObj;
            if (data == null || !data.TryGetValue("devices", out devicesObj)) return result;
            var items = devicesObj as IEnumerable;
            if (items == null) return result;
            foreach (object obj in items)
            {
                var item = obj as Dictionary<string, object>;
                if (item == null) continue;
                string id = GetString(item, "id", "");
                string name = CleanDeviceName(GetString(item, "name", id));
                if (!String.IsNullOrWhiteSpace(id) || !String.IsNullOrWhiteSpace(name))
                    result.Add(new AudioDevice { Id = id, Name = name });
            }
            return result;
        }

        private bool IsVlcEnded()
        {
            string json = QueryVlc("{\"command\":\"status\"}");
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            object ended;
            return data != null && data.TryGetValue("ended", out ended) && Convert.ToBoolean(ended);
        }

        private string EnsureMpvAvailable()
        {
            EnsurePythonRuntime(false);
            string helper = Path.Combine(libraryDir, "mpv_player.py");
            if (!File.Exists(helper)) throw new Exception("Arquivo do player MPV não encontrado.");
            string mpvDir = Path.Combine(runtimeDir, "MPV");
            bool hasLibMpv = File.Exists(Path.Combine(mpvDir, "libmpv-2.dll")) ||
                File.Exists(Path.Combine(mpvDir, "mpv-2.dll")) ||
                File.Exists(Path.Combine(mpvDir, "mpv-1.dll"));
            if (!hasLibMpv) throw new Exception("Runtime portátil do MPV não está disponível.");
            if (!CanStartPythonMpv()) throw new Exception("O MPV portátil não iniciou.");
            return helper;
        }

        private bool CanStartPythonMpv()
        {
            try
            {
                if (!File.Exists(Path.Combine(runtimeDir, "Python", "python.exe"))) return false;
                string mpvDir = Path.Combine(runtimeDir, "MPV");
                var psi = new ProcessStartInfo(GetPythonFileName(), "-c \"import os; os.add_dll_directory(r'" + EscapeArg(mpvDir) + "'); import mpv; p=mpv.MPV(video=False,ytdl=False,input_default_bindings=False,input_vo_keyboard=False,osc=False); print('ok')\"");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(prefix)) psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
                using (var process = Process.Start(psi))
                {
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Contains("ok");
                }
            }
            catch { return false; }
        }

        private void InstallNodeRuntimeIfMissing()
        {
            if (!String.IsNullOrEmpty(GetPortableTool(Path.Combine("Node", "node.exe")))) return;
            if (!String.IsNullOrEmpty(RunWhere("node.exe"))) return;
            if (WasInstallAttempted("node")) return;
            MarkInstallAttempted("node");
            RunProcess("winget", "install --id OpenJS.NodeJS -e --source winget --silent --accept-package-agreements --accept-source-agreements", 300000, false);
        }

        private string FindMpv()
        {
            string found = GetPortableTool(Path.Combine("MPV", "mpv.exe"));
            if (!String.IsNullOrEmpty(found)) return found;
            found = RunWhere("mpv.exe");
            if (!String.IsNullOrEmpty(found)) return found;
            var roots = new List<string>();
            roots.Add(Path.Combine(libraryDir, "tools"));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            foreach (string root in roots)
            {
                try
                {
                    if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                    string match = Directory.GetFiles(root, "mpv.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (!String.IsNullOrEmpty(match)) return match;
                }
                catch { }
            }
            return "";
        }

        private void StartMpvPlayback(string mediaPath)
        {
            string helper = Path.Combine(libraryDir, "mpv_player.py");
            if (!File.Exists(helper)) throw new Exception("Arquivo do player MPV não encontrado.");
            string mediaArg = WriteMpvMediaArgument(mediaPath);
            string args = "\"" + EscapeArg(helper) + "\" \"" + EscapeArg(mediaArg) + "\" " + savedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var psi = new ProcessStartInfo(GetPythonFileName(), args);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                psi.EnvironmentVariables["YOUTUBE_LIGHT_CONFIG_DIR"] = configDir;
                psi.EnvironmentVariables["YOUTUBE_LIGHT_LIBRARY_DIR"] = libraryDir;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(prefix)) psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
            }
            catch { }
            mpvProcess = Process.Start(psi);
            mpvInput = mpvProcess.StandardInput;
            usingMpv = true;
            string ready = ReadMpvLine(7000);
            if (String.IsNullOrWhiteSpace(ready) || !ready.Contains("\"ok\": true"))
            {
                string detail = ExtractJsonError(ready);
                try { if (mpvProcess != null && mpvProcess.HasExited) detail = FirstNonEmpty(detail, mpvProcess.StandardError.ReadToEnd().Trim()); } catch { }
                StopMpv();
                throw new Exception("O MPV não confirmou o início da reprodução" + (String.IsNullOrWhiteSpace(detail) ? "." : ": " + detail));
            }
            currentMediaPath = mediaPath;
            ApplySelectedOutputDeviceToMpv();
            StartPlayerMonitorIfNeeded(mediaPath);
        }

        private string WriteMpvMediaArgument(string mediaPath)
        {
            Directory.CreateDirectory(tempAudioDir);
            string sourceFile = Path.Combine(tempAudioDir, "mpv_source_" + Guid.NewGuid().ToString("N") + ".dat");
            File.WriteAllText(sourceFile, mediaPath ?? "", new UTF8Encoding(false));
            return "@" + sourceFile;
        }

        private void StopMpv()
        {
            try { SendMpvCommand("{\"command\":\"stop\"}"); } catch { }
            try
            {
                if (mpvProcess != null && !mpvProcess.HasExited)
                    mpvProcess.Kill();
            }
            catch { }
            mpvProcess = null;
            mpvInput = null;
            usingMpv = false;
            currentMediaPath = "";
        }

        private void SendMpvCommand(string json)
        {
            lock (mpvLock)
            {
                if (mpvProcess == null || mpvProcess.HasExited || mpvInput == null) throw new Exception("MPV não está aberto.");
                mpvInput.WriteLine(json);
                mpvInput.Flush();
            }
        }

        private string QueryMpv(string json)
        {
            lock (mpvLock)
            {
                if (mpvProcess == null || mpvProcess.HasExited || mpvInput == null) throw new Exception("MPV não está aberto.");
                mpvInput.WriteLine(json);
                mpvInput.Flush();
                return ReadMpvLine(1500);
            }
        }

        private string ReadMpvLine(int timeoutMs)
        {
            if (mpvProcess == null) return "";
            var task = Task.Factory.StartNew(delegate { return mpvProcess.StandardOutput.ReadLine(); });
            return task.Wait(timeoutMs) ? (task.Result ?? "") : "";
        }

        private double GetMpvNumberProperty(string property)
        {
            string command = property == "volume" ? "get-volume" : "get-time";
            string json = QueryMpv("{\"command\":\"" + command + "\"}");
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            object value;
            string key = property == "volume" ? "volume" : property == "duration" ? "duration" : "position";
            if (data != null && data.TryGetValue(key, out value))
                return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return 0;
        }

        private bool IsMpvEnded()
        {
            string json = QueryMpv("{\"command\":\"status\"}");
            var data = serializer.Deserialize<Dictionary<string, object>>(json);
            object ended;
            return data != null && data.TryGetValue("ended", out ended) && Convert.ToBoolean(ended);
        }

        private void PrefetchNext()
        {
            int next = currentIndex + 1;
            if (next < 0 || next >= tracks.Count) return;
            Track track = tracks[next];
            string url = track.Url;
            if (String.IsNullOrEmpty(url) && !String.IsNullOrEmpty(track.VideoId))
                url = "https://music.youtube.com/watch?v=" + track.VideoId;
            string cached;
            if (String.IsNullOrEmpty(url) || (streamCache.TryGetValue(url, out cached) && IsUsableMediaSource(cached))) return;
            RunWorker(delegate
            {
                try
                {
                ResolveStreamUrl(url);
                }
                catch { }
            }, true);
        }

        private void PlayRelative(int delta)
        {
            if (tracks.Count == 0) { SetStatus("Lista vazia."); return; }
            int next = currentIndex >= 0 ? currentIndex + delta : resultsList.SelectedIndex + delta;
            if (next < 0 || next >= tracks.Count) { AnnouncePlayerEvent("Não há item nessa direção."); return; }
            AnnouncePlayerEvent(delta > 0 ? "Indo para a próxima música." : "Indo para a música anterior.");
            resultsList.SelectedIndex = next;
            currentIndex = next;
            PlaySelected();
        }

        private void StopPlayback()
        {
            StopPlayback(true);
        }

        private void StopPlayback(bool announce)
        {
            try
            {
                suppressAutoAdvance = true;
                playbackStarted = false;
                playbackPaused = false;
                StopVlc();
                StopMpv();
                if (internalPlayer != null)
                    CallComMethod(GetComProperty(internalPlayer, "controls"), "stop");
            }
            catch { }
            DeleteFinishedTemporaryAudio();
            if (announce)
            {
                currentTrackTitle = "";
                currentTrackUrl = "";
                currentVideoId = "";
                currentTrackDuration = "";
                currentTrack = null;
                SetPlayerText("Player parado.");
                UpdateWindowTitle();
                SetProgress(false);
                SetStatus("Reprodução parada.");
            }
        }

        private void TogglePause()
        {
            SendPlayerCommand("pause-toggle");
            playbackPaused = !playbackPaused;
            UpdateWindowTitle();
            AnnouncePlayerEvent(playbackPaused ? "Pausado." : "Tocando.");
        }

        private void UpdateWindowTitle()
        {
            if (String.IsNullOrWhiteSpace(currentTrackTitle))
            {
                Text = "Youtube Light versão " + AppVersion;
                return;
            }
            Text = playbackPaused ? "Youtube Light, " + currentTrackTitle + ", pausado" : "Youtube Light, tocando " + currentTrackTitle;
        }

        private void SetupPlaybackTimer()
        {
            playbackTimer = new System.Windows.Forms.Timer();
            playbackTimer.Interval = 1000;
            playbackTimer.Tick += delegate { CheckAutoAdvance(); };
            playbackTimer.Start();
        }

        private void SetupHistoryCleanupTimer()
        {
            historyCleanupTimer = new System.Windows.Forms.Timer();
            historyCleanupTimer.Interval = 10 * 60 * 1000;
            historyCleanupTimer.Tick += delegate { ClearLocalHistoryByTimer(); ClearOldTemporaryAudioFiles(); };
            historyCleanupTimer.Start();
        }

        private void SetupNotificationTimer()
        {
            notificationTimer = new System.Windows.Forms.Timer();
            notificationTimer.Interval = Math.Max(1, notificationIntervalMinutes) * 60 * 1000;
            notificationTimer.Tick += delegate { CheckNewSubscriptionVideos(false); };
            if (realtimeVideoNotifications) notificationTimer.Start();
        }

        private void RestartNotificationTimer()
        {
            if (notificationTimer == null) return;
            notificationTimer.Stop();
            notificationTimer.Interval = Math.Max(1, notificationIntervalMinutes) * 60 * 1000;
            if (realtimeVideoNotifications) notificationTimer.Start();
        }

        private void CheckNewSubscriptionVideos(bool manual)
        {
            if (!manual && !realtimeVideoNotifications) return;
            if (manual) SetStatus("Verificando vídeos novos das inscrições.");
            RunWorker(delegate
            {
                try
                {
                    var found = TryLoadYoutubePageWithYtdlp("https://www.youtube.com/feed/subscriptions");
                    if (found.Count == 0)
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (manual) AnnounceStatus("Não consegui carregar as inscrições agora.");
                        }));
                        return;
                    }

                    bool firstAutomaticScan = notifiedVideoKeys.Count == 0 && !manual;
                    var newItems = new List<Track>();
                    foreach (Track track in found)
                    {
                        string key = TrackKey(track);
                        if (String.IsNullOrWhiteSpace(key)) continue;
                        if (!firstAutomaticScan && !notifiedVideoKeys.Contains(key)) newItems.Add(track);
                    }

                    foreach (Track track in found)
                    {
                        string key = TrackKey(track);
                        if (!String.IsNullOrWhiteSpace(key)) notifiedVideoKeys.Add(key);
                    }
                    SaveNotifiedVideos();

                    BeginInvoke(new Action(delegate
                    {
                        if (firstAutomaticScan)
                        {
                            SetStatus("Notificações de vídeos novos iniciadas.");
                            return;
                        }
                        if (newItems.Count == 0)
                        {
                            if (manual) AnnounceStatus("Nenhum vídeo novo nas inscrições.");
                            return;
                        }
                        string message = BuildNewVideosMessage(newItems.Take(5).ToList());
                        SetStatus(message);
                        if (autoReadVideoNotifications || manual) Speak(message);
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (manual) AnnounceStatus("Não consegui verificar vídeos novos. Detalhe: " + ShortError(ex.Message));
                    }));
                }
            }, true);
        }

        private string BuildNewVideosMessage(List<Track> items)
        {
            var builder = new StringBuilder();
            builder.Append("Vídeos novos: ");
            for (int i = 0; i < items.Count; i++)
            {
                Track track = items[i];
                if (i > 0) builder.Append(" ");
                builder.Append(track.Title);
                if (!String.IsNullOrWhiteSpace(track.Channel)) builder.Append(", " + track.Channel);
                if (!String.IsNullOrWhiteSpace(track.Duration)) builder.Append(", " + track.Duration);
                string published = HumanPublished(track.Published);
                if (!String.IsNullOrWhiteSpace(published)) builder.Append(", postado " + published);
                builder.Append(".");
            }
            return builder.ToString();
        }

        private void ClearLocalHistoryByTimer()
        {
            if (localHistory.Count == 0) return;
            localHistory.Clear();
            SaveLocalData();
            SetStatus("Histórico local limpo automaticamente.");
        }

        private void CheckAutoAdvance()
        {
            if (!playbackStarted || suppressAutoAdvance) return;
            if (!autoplayEnabled) return;
            try
            {
                if (usingVlc)
                {
                    bool vlcEnded = vlcProcess == null || vlcProcess.HasExited || IsVlcEnded();
                    if (vlcEnded && playbackStartedAt != DateTime.MinValue && (DateTime.Now - playbackStartedAt).TotalSeconds > 5)
                    {
                        if (currentIndex + 1 < tracks.Count)
                        {
                            playbackStarted = false;
                            AutoPlayNext();
                        }
                        else
                        {
                            playbackStarted = false;
                            HandleEndOfPlaybackList();
                        }
                    }
                    return;
                }
                if (usingMpv)
                {
                    bool mpvEnded = mpvProcess == null || mpvProcess.HasExited || IsMpvEnded();
                    if (mpvEnded && playbackStartedAt != DateTime.MinValue && (DateTime.Now - playbackStartedAt).TotalSeconds > 5)
                    {
                        if (currentIndex + 1 < tracks.Count)
                        {
                            playbackStarted = false;
                            AutoPlayNext();
                        }
                        else
                        {
                            playbackStarted = false;
                            HandleEndOfPlaybackList();
                        }
                    }
                    return;
                }
                if (usingFfplay)
                {
                    bool ffplayEnded = ffplayProcess == null || ffplayProcess.HasExited;
                    if (ffplayEnded && playbackStartedAt != DateTime.MinValue && (DateTime.Now - playbackStartedAt).TotalSeconds > 5)
                    {
                        if (currentIndex + 1 < tracks.Count)
                        {
                            playbackStarted = false;
                            AutoPlayNext();
                        }
                        else
                        {
                            playbackStarted = false;
                            HandleEndOfPlaybackList();
                        }
                    }
                    return;
                }
                if (internalPlayer == null) return;
                int playState = Convert.ToInt32(GetComProperty(internalPlayer, "playState"));
                bool ended = playState == 8;
                bool stoppedAfterStart = playState == 1 && playbackStartedAt != DateTime.MinValue && (DateTime.Now - playbackStartedAt).TotalSeconds > 5;
                if (ended || stoppedAfterStart)
                {
                    if (currentIndex + 1 < tracks.Count)
                    {
                        playbackStarted = false;
                        AutoPlayNext();
                    }
                    else
                    {
                        playbackStarted = false;
                        HandleEndOfPlaybackList();
                    }
                }
            }
            catch { }
        }

        private void AutoPlayNext()
        {
            if (playbackQueue.Count > 0)
            {
                Track nextQueued = playbackQueue[0];
                playbackQueue.RemoveAt(0);
                SaveLocalData();
                AnnouncePlayerEvent("Tocando próximo item da fila.");
                currentIndex = -1;
                PlayTrack(nextQueued);
                return;
            }
            if (localFolderAudioOnly && tracks.Count > 0)
            {
                if (localFolderPlaybackMode == "repeat_once" && !repeatOnceConsumed && currentIndex >= 0 && currentIndex < tracks.Count)
                {
                    repeatOnceConsumed = true;
                    PlayTrack(tracks[currentIndex]);
                    return;
                }
                if (localFolderPlaybackMode == "repeat_all")
                {
                    int nextLoop = currentIndex + 1 < tracks.Count ? currentIndex + 1 : 0;
                    currentIndex = nextLoop;
                    if (resultsList != null && resultsList.Items.Count > nextLoop) resultsList.SelectedIndex = nextLoop;
                    PlayTrack(tracks[nextLoop]);
                    return;
                }
                if (localFolderPlaybackMode == "shuffle")
                {
                    int nextRandom = new Random().Next(0, tracks.Count);
                    currentIndex = nextRandom;
                    if (resultsList != null && resultsList.Items.Count > nextRandom) resultsList.SelectedIndex = nextRandom;
                    PlayTrack(tracks[nextRandom]);
                    return;
                }
            }
            if (tracks.Count > 0 && currentIndex + 1 < tracks.Count)
            {
                repeatOnceConsumed = false;
                AnnouncePlayerEvent("Tocando próxima música automaticamente.");
                PlayRelative(1);
                return;
            }
            HandleEndOfPlaybackList();
        }

        private void HandleEndOfPlaybackList()
        {
            if (infiniteRadio && !String.IsNullOrWhiteSpace(currentVideoId))
            {
                AnnouncePlayerEvent("Carregando rádio infinita.");
                string seed = currentVideoId;
                RunWorker(delegate
                {
                    try
                    {
                        var radio = TracksFromBridge("watch", seed);
                        BeginInvoke(new Action(delegate
                        {
                            ReplaceList(radio, "Rádio infinita carregada.");
                            if (radio.Count > 0)
                            {
                                currentIndex = 0;
                                PlayTrack(radio[0]);
                            }
                        }));
                    }
                    catch { BeginInvoke(new Action(delegate { SetStatus("Fim da lista."); })); }
                }, true);
            }
            else SetStatus("Fim da lista.");
        }

        private void AnnouncePlayerEvent(string message)
        {
            if (announcePlayerEvents) AnnounceStatus(message);
            else SetStatus(message);
        }

        private void AnnounceVolume()
        {
            AnnounceVolume(true);
        }

        private void AnnounceVolume(bool speak)
        {
            if (usingVlc)
            {
                try
                {
                    int value = ClampAppVolume((int)Math.Round(GetVlcNumber("get-volume", "volume")));
                    savedVolume = value;
                    hasSavedVolume = true;
                    SaveConfig();
                    SetOrAnnounce("Volume " + value + " por cento.", speak);
                }
                catch { SetOrAnnounce("Volume " + savedVolume + " por cento.", speak); }
                return;
            }
            if (usingMpv)
            {
                try
                {
                    int value = ClampAppVolume((int)Math.Round(GetMpvNumberProperty("volume")));
                    savedVolume = value;
                    hasSavedVolume = true;
                    SaveConfig();
                    SetOrAnnounce("Volume " + value + " por cento.", speak);
                }
                catch { SetOrAnnounce("Volume " + savedVolume + " por cento.", speak); }
                return;
            }
            if (!EnsureInternalPlayer()) return;
            int wmpVolume = GetPlayerVolume();
            savedVolume = wmpVolume;
            hasSavedVolume = true;
            SaveConfig();
            SetOrAnnounce("Volume " + wmpVolume + " por cento.", speak);
        }

        private void SetOrAnnounce(string message, bool speak)
        {
            if (speak) AnnounceStatus(message);
            else SetStatus(message);
        }

        private void AnnounceTime()
        {
            if (usingVlc)
            {
                try
                {
                    string json = QueryVlc("{\"command\":\"get-time\"}");
                    var data = serializer.Deserialize<Dictionary<string, object>>(json);
                    double pos = data != null && data.ContainsKey("position") ? Convert.ToDouble(data["position"], System.Globalization.CultureInfo.InvariantCulture) : 0;
                    double dur = data != null && data.ContainsKey("duration") ? Convert.ToDouble(data["duration"], System.Globalization.CultureInfo.InvariantCulture) : 0;
                    if (dur <= 0) dur = ParseDurationSeconds(currentTrackDuration);
                    if (dur > 0)
                    {
                        double remaining = Math.Max(0, dur - pos);
                        AnnounceStatus("Tempo atual " + FormatTime(pos) + ". Faltam " + FormatTime(remaining) + ".");
                    }
                    else AnnounceStatus("Tempo atual " + FormatTime(pos) + ".");
                }
                catch { AnnounceStatus("Não consegui ler o tempo atual."); }
                return;
            }
            if (usingMpv)
            {
                try
                {
                    double pos = GetMpvNumberProperty("time-pos");
                    double dur = GetMpvNumberProperty("duration");
                    if (dur <= 0) dur = ParseDurationSeconds(currentTrackDuration);
                    if (dur > 0)
                    {
                        double remaining = Math.Max(0, dur - pos);
                        AnnounceStatus("Tempo atual " + FormatTime(pos) + ". Faltam " + FormatTime(remaining) + ".");
                    }
                    else AnnounceStatus("Tempo atual " + FormatTime(pos) + ".");
                }
                catch { AnnounceStatus("Não consegui ler o tempo atual."); }
                return;
            }
            if (!EnsureInternalPlayer()) return;
            object controls = GetComProperty(internalPlayer, "controls");
            object media = GetComProperty(internalPlayer, "currentMedia");
            string position = Convert.ToString(GetComProperty(controls, "currentPosition"), System.Globalization.CultureInfo.InvariantCulture);
            string duration = media == null ? "" : Convert.ToString(GetComProperty(media, "duration"), System.Globalization.CultureInfo.InvariantCulture);
            double wmpPos;
            double wmpDur;
            if (!Double.TryParse(position, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out wmpPos))
            {
                AnnounceStatus("Não consegui ler o tempo atual.");
                return;
            }
            if (!Double.TryParse(duration, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out wmpDur) || wmpDur <= 0)
                wmpDur = ParseDurationSeconds(currentTrackDuration);
            if (wmpDur > 0)
            {
                double remaining = Math.Max(0, wmpDur - wmpPos);
                AnnounceStatus("Tempo atual " + FormatTime(wmpPos) + ". Faltam " + FormatTime(remaining) + ".");
            }
            else
            {
                AnnounceStatus("Tempo atual " + FormatTime(wmpPos) + ".");
            }
        }

        private void AnnounceTitle()
        {
            if (String.IsNullOrWhiteSpace(currentTrackTitle)) AnnounceStatus("Nenhuma música tocando.");
            else
            {
                Clipboard.SetText(currentTrackTitle);
                AnnounceStatus("Título: " + currentTrackTitle + ". Copiado para a área de transferência.");
            }
        }

        private void SetPlayerText(string text)
        {
            if (playerList == null) return;
            playerList.Items.Clear();
            playerList.Items.Add(text);
            playerList.SelectedIndex = 0;
        }

        private void CopyCurrentLink()
        {
            if (String.IsNullOrWhiteSpace(currentTrackUrl))
            {
                AnnounceStatus("Nenhum link de música para copiar.");
                return;
            }
            Clipboard.SetText(currentTrackUrl);
            AnnounceStatus("Link copiado.");
        }

        private string FormatNumber(string raw)
        {
            double value;
            if (!Double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)) return raw;
            return Math.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private string SafeFileName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "audio";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        private string ShortError(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return "erro desconhecido.";
            string first = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;
            if (first.Length > 180) first = first.Substring(0, 180) + "...";
            return first;
        }

        private string FormatTime(double seconds)
        {
            int total = (int)Math.Round(seconds);
            int minutes = total / 60;
            int secs = total % 60;
            int hours = minutes / 60;
            minutes = minutes % 60;
            if (hours > 0) return hours + " hora, " + minutes + " minuto e " + secs + " segundo";
            return minutes + " minuto e " + secs + " segundo";
        }

        private double ParseDurationSeconds(string duration)
        {
            if (String.IsNullOrWhiteSpace(duration)) return 0;
            string[] parts = duration.Split(':');
            double total = 0;
            foreach (string part in parts)
            {
                int value;
                if (!Int32.TryParse(part.Trim(), out value)) return 0;
                total = total * 60 + value;
            }
            return total;
        }

        private void SendPlayerCommand(params string[] parts)
        {
            if (usingVlc)
            {
                try
                {
                    string command = parts.Length > 0 ? parts[0] : "";
                    if (command == "pause-toggle")
                    {
                        SendVlcCommand("{\"command\":\"pause-toggle\"}", false);
                        SendPlayerMonitorCommand("{\"command\":\"pause-toggle\"}", false);
                    }
                    else if (command == "seek")
                    {
                        SendVlcCommand("{\"command\":\"seek\",\"delta\":" + parts[1] + "}", false);
                        SendPlayerMonitorCommand("{\"command\":\"seek\",\"delta\":" + parts[1] + "}", false);
                    }
                    else if (command == "seek-to")
                    {
                        SendVlcCommand("{\"command\":\"seek-to\",\"seconds\":" + parts[1] + "}", false);
                        SendPlayerMonitorCommand("{\"command\":\"seek-to\",\"seconds\":" + parts[1] + "}", false);
                    }
                    else if (command == "add" && parts.Length >= 3 && parts[1] == "volume")
                    {
                        int delta = Int32.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                        savedVolume = ClampAppVolume(savedVolume + delta);
                        hasSavedVolume = true;
                        SaveConfig();
                        SendVlcCommand("{\"command\":\"set-volume\",\"volume\":" + savedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", false);
                    }
                }
                catch (Exception ex) { SetStatus("Não consegui controlar o VLC: " + ex.Message); }
                return;
            }
            if (usingMpv)
            {
                try
                {
                    string command = parts.Length > 0 ? parts[0] : "";
                    if (command == "pause-toggle")
                    {
                        SendMpvCommand("{\"command\":\"pause-toggle\"}");
                        SendPlayerMonitorCommand("{\"command\":\"pause-toggle\"}", false);
                    }
                    else if (command == "seek")
                    {
                        SendMpvCommand("{\"command\":\"seek\",\"delta\":" + parts[1] + "}");
                        SendPlayerMonitorCommand("{\"command\":\"seek\",\"delta\":" + parts[1] + "}", false);
                    }
                    else if (command == "seek-to")
                    {
                        SendMpvCommand("{\"command\":\"seek-to\",\"seconds\":" + parts[1] + "}");
                        SendPlayerMonitorCommand("{\"command\":\"seek-to\",\"seconds\":" + parts[1] + "}", false);
                    }
                    else if (command == "add" && parts.Length >= 3 && parts[1] == "volume")
                    {
                        int delta = Int32.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                        savedVolume = ClampAppVolume(savedVolume + delta);
                        hasSavedVolume = true;
                        SaveConfig();
                        SendMpvCommand("{\"command\":\"set-volume\",\"volume\":" + savedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                        SendPlayerMonitorCommand("{\"command\":\"set-volume\",\"volume\":" + savedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", false);
                    }
                }
                catch (Exception ex) { SetStatus("Não consegui controlar o MPV: " + ex.Message); }
                return;
            }
            if (!EnsureInternalPlayer()) return;
            try
            {
                object controls = GetComProperty(internalPlayer, "controls");
                object settings = GetComProperty(internalPlayer, "settings");
                string command = parts.Length > 0 ? parts[0] : "";
                if (command == "pause-toggle")
                {
                    int playState = Convert.ToInt32(GetComProperty(internalPlayer, "playState"));
                    if (playState == 3) CallComMethod(controls, "pause");
                    else CallComMethod(controls, "play");
                }
                else if (command == "seek")
                {
                    double delta = Double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    double current = Convert.ToDouble(GetComProperty(controls, "currentPosition"), System.Globalization.CultureInfo.InvariantCulture);
                    SetComProperty(controls, "currentPosition", Math.Max(0, current + delta));
                }
                else if (command == "seek-to")
                {
                    double seconds = Double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    SetComProperty(controls, "currentPosition", Math.Max(0, seconds));
                }
                else if (command == "add" && parts.Length >= 3 && parts[1] == "volume")
                {
                    int delta = Int32.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    int volume = Convert.ToInt32(GetComProperty(settings, "volume"));
                    savedVolume = Math.Max(0, Math.Min(100, volume + delta));
                    hasSavedVolume = true;
                    SetComProperty(settings, "volume", savedVolume);
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                SetStatus("Não consegui controlar o player: " + ex.Message);
            }
        }

        private void DownloadSelected()
        {
            DownloadSelectedAsAudio();
        }

        private void DownloadSelectedWithFormat()
        {
            DownloadTrackWithFormat(SelectedTrack());
        }

        private void DownloadSelectedAsAudio()
        {
            DownloadTrackAsAudio(SelectedTrack());
        }

        private void DownloadSelectedAsVideo()
        {
            DownloadTrackAsVideo(SelectedTrack());
        }

        private void DownloadTrackAsAudio(Track track)
        {
            if (track == null) return;
            AnnounceStatus("Baixando musica " + track.Title + ".");
            DownloadTrackAs(track, "mp3");
        }

        private void DownloadTrackAsVideo(Track track)
        {
            if (track == null) return;
            AnnounceStatus("Baixando video " + track.Title + ".");
            if (track.Kind == "playlist") { SetStatus("Abra a playlist e baixe os vídeos individualmente."); return; }
            string url = track.Url;
            if (String.IsNullOrEmpty(url) && !String.IsNullOrEmpty(track.VideoId))
                url = "https://www.youtube.com/watch?v=" + track.VideoId;
            if (String.IsNullOrEmpty(url)) { SetStatus("Este item não tem URL para baixar."); return; }
            DownloadUrlAsVideo(url, track.Title);
        }

        private void DownloadTrackWithFormat(Track track)
        {
            if (track == null) return;
            string[] formats = new[] { "original", "mp3", "m4a", "opus", "wav", "flac" };
            using (var form = new Form())
            {
                form.Text = "Baixar";
                form.Size = new Size(420, 300);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Lista de formatos";
                foreach (string format in formats) list.Items.Add(format);
                list.SelectedIndex = 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Baixar", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null)
                    DownloadTrackAs(track, list.SelectedItem.ToString());
            }
        }

        private void SeekToSeconds(double seconds)
        {
            SendPlayerCommand("seek-to", Math.Max(0, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private void DownloadSelectedAs(string format)
        {
            Track track = SelectedTrack();
            if (track == null) return;
            DownloadTrackAs(track, format);
        }

        private void DownloadTrackAs(Track track, string format)
        {
            if (track == null) return;
            if (track.Kind == "playlist") { SetStatus("Abra a playlist e baixe as faixas individualmente."); return; }
            string url = track.Url;
            if (String.IsNullOrEmpty(url) && !String.IsNullOrEmpty(track.VideoId))
                url = "https://music.youtube.com/watch?v=" + track.VideoId;
            if (String.IsNullOrEmpty(url)) { SetStatus("Este item não tem URL para baixar."); return; }
            DownloadUrlAs(url, track.Title, format);
        }

        private void DownloadByLink()
        {
            using (var form = new Form())
            {
                form.Text = "Baixar por link";
                form.Size = new Size(560, 220);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);

                var label = new Label();
                label.Text = "Cole o link do YouTube ou YouTube Music.";
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);

                var box = new TextBox();
                box.AccessibleName = "Link para baixar";
                box.Dock = DockStyle.Top;
                panel.Controls.Add(box, 0, 1);

                var ok = new Button { Text = "Continuar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;

                if (form.ShowDialog(this) != DialogResult.OK) return;
                string url = box.Text.Trim();
                if (String.IsNullOrWhiteSpace(url)) { AnnounceStatus("Digite um link primeiro."); return; }
                string format = ChooseDownloadFormat();
                if (String.IsNullOrWhiteSpace(format)) return;
                DownloadUrlAs(url, "link informado", format);
            }
        }

        private void OpenYoutubeLink()
        {
            using (var form = new Form())
            {
                form.Text = "Abrir link do YouTube";
                form.Size = new Size(560, 220);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);

                var label = new Label();
                label.Text = "Cole o link do vídeo, playlist ou canal.";
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);

                var box = new TextBox();
                box.AccessibleName = "Link do YouTube";
                box.Dock = DockStyle.Top;
                panel.Controls.Add(box, 0, 1);

                var ok = new Button { Text = "Abrir", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;

                if (form.ShowDialog(this) != DialogResult.OK) return;
                string url = box.Text.Trim();
                if (String.IsNullOrWhiteSpace(url)) { AnnounceStatus("Digite um link primeiro."); return; }
                var track = new Track { Kind = GuessYoutubeLinkKind(url), Title = "Link informado", Url = url };
                if (track.Kind == "channel") LoadChannelVideos(track);
                else if (track.Kind == "playlist") LoadYoutubePlaylist(track);
                else PlayTrack(track);
            }
        }

        private string GuessYoutubeLinkKind(string url)
        {
            string lower = (url ?? "").ToLowerInvariant();
            if (lower.Contains("list=") || lower.Contains("/playlist")) return "playlist";
            if (lower.Contains("/channel/") || lower.Contains("/@") || lower.Contains("/c/") || lower.Contains("/user/")) return "channel";
            return "track";
        }

        private void OpenLocalMediaFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Abrir mídia do computador";
                dialog.Filter = "Mídia|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv|Todos os arquivos|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var track = TrackFromLocalFile(dialog.FileName);
                ReplaceList(new List<Track> { track }, "Arquivo local carregado.");
                PlayTrack(track);
            }
        }

        private void LoadLocalMediaFolder(bool playFirst)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Escolha a pasta com músicas ou vídeos.";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var found = LoadLocalMediaFiles(dialog.SelectedPath);
                localFolderAudioOnly = found.Count > 0 && found.All(t => IsAudioFile(t.Url));
                localFolderPlaybackMode = "normal";
                repeatOnceConsumed = false;
                ReplaceList(found, "Pasta de mídia carregada.");
                if (playFirst && found.Count > 0)
                {
                    currentIndex = 0;
                    BeginInvoke(new Action(delegate
                    {
                        if (resultsList != null && resultsList.Items.Count > 0) resultsList.SelectedIndex = 0;
                        PlayTrack(found[0]);
                    }));
                }
            }
        }

        private List<Track> LoadLocalMediaFiles(string folder)
        {
            var result = new List<Track>();
            try
            {
                foreach (string file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                    if (IsLocalMediaFile(file)) result.Add(TrackFromLocalFile(file));
            }
            catch (Exception ex)
            {
                SetStatus("Não consegui ler a pasta: " + ShortError(ex.Message));
            }
            return result.OrderBy(t => t.Title).ToList();
        }

        private Track TrackFromLocalFile(string file)
        {
            return new Track
            {
                Kind = "track",
                Title = Path.GetFileNameWithoutExtension(file),
                Channel = "Arquivo local",
                Duration = "",
                Url = file,
                VideoId = "",
                LikeStatus = ""
            };
        }

        private bool IsLocalMediaFile(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return new[]
            {
                ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma",
                ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"
            }.Contains(ext);
        }

        private bool IsAudioFile(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma" }.Contains(ext);
        }

        private void CycleLocalFolderPlaybackMode()
        {
            if (!localFolderAudioOnly)
            {
                AnnounceStatus("O modo de repetição com R está disponível quando a pasta carregada tem apenas áudio.");
                return;
            }
            if (localFolderPlaybackMode == "normal") localFolderPlaybackMode = "repeat_once";
            else if (localFolderPlaybackMode == "repeat_once") localFolderPlaybackMode = "repeat_all";
            else if (localFolderPlaybackMode == "repeat_all") localFolderPlaybackMode = "shuffle";
            else localFolderPlaybackMode = "normal";
            repeatOnceConsumed = false;
            string label = localFolderPlaybackMode == "repeat_once" ? "Repetir uma vez" :
                localFolderPlaybackMode == "repeat_all" ? "Repetir infinitamente" :
                localFolderPlaybackMode == "shuffle" ? "Modo aleatório" : "Reprodução normal";
            AnnounceStatus(label + ".");
        }

        private string ChooseDownloadFormat()
        {
            string[] formats = new[] { "original", "mp3", "m4a", "opus", "wav", "flac" };
            using (var form = new Form())
            {
                form.Text = "Formato do download";
                form.Size = new Size(420, 300);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Lista de formatos";
                foreach (string item in formats) list.Items.Add(item);
                list.SelectedIndex = 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Baixar", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null)
                    return list.SelectedItem.ToString();
            }
            return "";
        }

        private void DownloadUrlAs(string url, string title, string format)
        {
            if (format != "original" && !EnsureFfmpeg()) return;
            Directory.CreateDirectory(GetDownloadDir());
            SetProgress(true);
            AnnounceStatus("Baixando musica " + title + " em " + format + ".");
            AnnounceStatus("Baixando musica " + title + " em " + format + ".");
            SetStatus("Baixando " + title + " em " + format + ".");
            RunWorker(delegate
            {
                try
                {
                    string template = Path.Combine(GetDownloadDir(), "%(title)s.%(ext)s");
                    string args = GetYtdlpCookieArgs() + GetYtdlpYoutubeArgs() + "--newline -f bestaudio/best --no-playlist -o \"" + EscapeArg(template) + "\" ";
                    if (format != "original")
                        args += "-x --audio-format " + format + " ";
                    args += "\"" + EscapeArg(url) + "\"";
                    RunYtdlpWithProgress(args, 0, "Download");
                    BeginInvoke(new Action(delegate { SetProgress(false); AnnounceStatus("Download concluído: " + title + "."); }));
                }
                catch
                {
                    BeginInvoke(new Action(delegate { SetProgress(false); }));
                    throw;
                }
            });
        }

        private void DownloadUrlAsVideo(string url, string title)
        {
            if (!EnsureFfmpeg()) return;
            Directory.CreateDirectory(GetDownloadDir());
            SetProgress(true);
            SetStatus("Baixando vídeo: " + title + ".");
            RunWorker(delegate
            {
                try
                {
                    string template = Path.Combine(GetDownloadDir(), "%(title)s.%(ext)s");
                    string args = GetYtdlpCookieArgs() + GetYtdlpYoutubeArgs() + "--newline -f \"bestvideo*+bestaudio/best\" --merge-output-format mp4 --no-playlist -o \"" + EscapeArg(template) + "\" \"" + EscapeArg(url) + "\"";
                    RunYtdlpWithProgress(args, 0, "Download");
                    BeginInvoke(new Action(delegate { SetProgress(false); AnnounceStatus("Download de vídeo concluído: " + title + "."); }));
                }
                catch
                {
                    BeginInvoke(new Action(delegate { SetProgress(false); }));
                    throw;
                }
            });
        }

        private bool EnsureFfmpeg()
        {
            if (!String.IsNullOrEmpty(GetPortableTool(Path.Combine("FFmpeg", "bin", "ffmpeg.exe")))) return true;
            if (!String.IsNullOrEmpty(RunWhere("ffmpeg.exe"))) return true;
            DialogResult answer = MessageBox.Show(
                "Para converter formatos como mp3, wav e flac, preciso instalar FFmpeg pelo winget. Posso instalar agora?",
                Text,
                MessageBoxButtons.YesNo);
            if (answer != DialogResult.Yes) return false;
            SetStatus("Instalando FFmpeg.");
            RunWorker(delegate
            {
                RunProcess("winget", "install --id Gyan.FFmpeg -e --source winget", 0);
                BeginInvoke(new Action(delegate { SetStatus("FFmpeg instalado. Tente baixar novamente."); }));
            });
            return false;
        }

        private string GetFfmpegFileName()
        {
            string portable = GetPortableTool(Path.Combine("FFmpeg", "bin", "ffmpeg.exe"));
            if (!String.IsNullOrWhiteSpace(portable)) return portable;
            string found = RunWhere("ffmpeg.exe");
            return String.IsNullOrWhiteSpace(found) ? "ffmpeg" : found;
        }

        private void ConvertMediaFile(bool audioToVideo)
        {
            if (!EnsureFfmpeg()) return;
            using (var open = new OpenFileDialog())
            {
                open.Title = audioToVideo ? "Escolher áudio para virar vídeo" : "Escolher arquivo para converter";
                open.Filter = "Mídia|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv|Todos os arquivos|*.*";
                if (open.ShowDialog(this) != DialogResult.OK) return;

                string format = audioToVideo ? "mp4" : ChooseConversionFormat();
                if (String.IsNullOrWhiteSpace(format)) return;
                string imagePath = audioToVideo ? ChooseOptionalVideoImage() : "";

                using (var save = new SaveFileDialog())
                {
                    save.Title = "Salvar convertido";
                    save.Filter = format.ToUpperInvariant() + "|*." + format + "|Todos os arquivos|*.*";
                    save.FileName = Path.GetFileNameWithoutExtension(open.FileName) + "_convertido." + format;
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    RunMediaConversion(open.FileName, save.FileName, format, audioToVideo, imagePath);
                }
            }
        }

        private string ChooseOptionalVideoImage()
        {
            DialogResult answer = MessageBox.Show(
                "Quer escolher uma imagem do computador para aparecer no vídeo? Se escolher Não, uso fundo preto.",
                Text,
                MessageBoxButtons.YesNo);
            if (answer != DialogResult.Yes) return "";
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Escolher imagem do vídeo";
                dialog.Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Todos os arquivos|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) return dialog.FileName;
            }
            return "";
        }

        private string ChooseConversionFormat()
        {
            using (var form = new Form())
            {
                form.Text = "Formato de conversão";
                form.Size = new Size(420, 340);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Formato de saída";
                foreach (string item in new[] { "mp3", "m4a", "opus", "wav", "flac", "mp4", "mkv", "webm" }) list.Items.Add(item);
                list.SelectedIndex = 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Converter", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                };
                if (form.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null) return list.SelectedItem.ToString();
            }
            return "";
        }

        private void RunMediaConversion(string input, string output, string format, bool audioToVideo, string imagePath)
        {
            SetProgress(true);
            SetStatus("Convertendo arquivo.");
            RunWorker(delegate
            {
                try
                {
                    string ffmpeg = GetFfmpegFileName();
                    string args;
                    if (audioToVideo)
                    {
                        if (!String.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                            args = "-y -loop 1 -i \"" + EscapeArg(imagePath) + "\" -i \"" + EscapeArg(input) + "\" -shortest -vf \"scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2\" -c:v libx264 -preset ultrafast -tune stillimage -c:a aac -b:a 192k -pix_fmt yuv420p \"" + EscapeArg(output) + "\"";
                        else
                            args = "-y -f lavfi -i color=c=black:s=1280x720:r=30 -i \"" + EscapeArg(input) + "\" -shortest -c:v libx264 -preset ultrafast -tune stillimage -c:a aac -b:a 192k -pix_fmt yuv420p \"" + EscapeArg(output) + "\"";
                    }
                    else if (format == "mp3" || format == "m4a" || format == "opus" || format == "wav" || format == "flac")
                    {
                        args = "-y -i \"" + EscapeArg(input) + "\" -vn \"" + EscapeArg(output) + "\"";
                    }
                    else
                    {
                        args = "-y -i \"" + EscapeArg(input) + "\" -c:v copy -c:a copy \"" + EscapeArg(output) + "\"";
                    }
                    try
                    {
                        RunProcess(ffmpeg, args, 0);
                    }
                    catch
                    {
                        if (audioToVideo || format == "mp3" || format == "m4a" || format == "opus" || format == "wav" || format == "flac") throw;
                        string fallback = "-y -i \"" + EscapeArg(input) + "\" -c:v libx264 -preset ultrafast -c:a aac -b:a 192k \"" + EscapeArg(output) + "\"";
                        RunProcess(ffmpeg, fallback, 0);
                    }
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Conversão concluída.");
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Não consegui converter. Detalhe: " + ShortError(ex.Message));
                    }));
                }
            }, true);
        }

        private bool IsResultsHotkeyContext()
        {
            return resultsPanel != null && resultsList != null && resultsPanel.Visible && resultsList.Visible && resultsList.SelectedIndex >= 0 && resultsList.SelectedIndex < tracks.Count;
        }

        private Track SelectedTrack()
        {
            if (resultsList.SelectedIndex < 0 || resultsList.SelectedIndex >= tracks.Count)
            {
                SetStatus("Selecione um resultado primeiro.");
                return null;
            }
            return tracks[resultsList.SelectedIndex];
        }

        private Track CurrentTrackForActions()
        {
            if (currentTrack != null) return currentTrack;
            if (!String.IsNullOrWhiteSpace(currentTrackTitle) || !String.IsNullOrWhiteSpace(currentTrackUrl))
                return new Track { Title = currentTrackTitle, Url = currentTrackUrl, VideoId = currentVideoId, Duration = currentTrackDuration };
            return null;
        }

        private string TrackUrl(Track track)
        {
            if (track == null) return "";
            if (!String.IsNullOrEmpty(track.Url)) return track.Url;
            if (!String.IsNullOrEmpty(track.VideoId)) return "https://music.youtube.com/watch?v=" + track.VideoId;
            return "";
        }

        private Track CloneTrack(Track track)
        {
            if (track == null) return null;
            return new Track
            {
                Kind = track.Kind,
                Title = track.Title,
                Channel = track.Channel,
                Duration = track.Duration,
                Url = TrackUrl(track),
                VideoId = track.VideoId,
                BrowseId = track.BrowseId,
                PlaylistId = track.PlaylistId,
                LikeStatus = track.LikeStatus,
                Published = track.Published
            };
        }

        private string TrackKey(Track track)
        {
            if (track == null) return "";
            if (!String.IsNullOrWhiteSpace(track.VideoId)) return "id:" + track.VideoId;
            return "url:" + TrackUrl(track).ToLowerInvariant();
        }

        private void QueueSelected(bool next)
        {
            Track track = SelectedTrack();
            if (track == null) return;
            QueueTrack(track, next);
        }

        private void QueueTrack(Track track, bool next)
        {
            if (track == null) return;
            Track copy = CloneTrack(track);
            if (next) playbackQueue.Insert(0, copy);
            else playbackQueue.Add(copy);
            SaveLocalData();
            SetStatus(next ? "Música colocada para tocar a seguir." : "Música adicionada ao fim da fila.");
        }

        private void AddSelectedToLocalFavorites()
        {
            Track track = SelectedTrack();
            if (track == null) return;
            AddTrackToLocalFavorites(track);
        }

        private void AddTrackToLocalFavorites(Track track)
        {
            if (track == null) return;
            string key = TrackKey(track);
            if (!localFavorites.Any(t => TrackKey(t) == key))
                localFavorites.Insert(0, CloneTrack(track));
            SaveLocalData();
            SetStatus("Adicionado aos favoritos locais.");
        }

        private void RemoveSelectedFromLocalFavorites()
        {
            Track track = SelectedTrack();
            if (track == null) return;
            RemoveTrackFromLocalFavorites(track);
        }

        private void RemoveTrackFromLocalFavorites(Track track)
        {
            if (track == null) return;
            string key = TrackKey(track);
            localFavorites.RemoveAll(t => TrackKey(t) == key);
            SaveLocalData();
            SetStatus("Removido dos favoritos locais.");
        }

        private void AddToLocalHistory(Track track)
        {
            Track copy = CloneTrack(track);
            if (copy == null) return;
            string key = TrackKey(copy);
            localHistory.RemoveAll(t => TrackKey(t) == key);
            localHistory.Insert(0, copy);
            while (localHistory.Count > 200) localHistory.RemoveAt(localHistory.Count - 1);
            SaveLocalData();
        }

        private void RateSelected(string rating)
        {
            Track track = SelectedTrack();
            if (track == null) return;
            RateTrack(track, rating);
        }

        private void RateTrack(Track track, string rating)
        {
            if (track == null) return;
            if (String.IsNullOrEmpty(track.VideoId))
            {
                SetStatus("Este item não pode ser avaliado.");
                return;
            }
            SetStatus("Enviando avaliação.");
            RunWorker(delegate
            {
                RunBridge("rate", track.VideoId, rating);
                BeginInvoke(new Action(delegate
                {
                    track.LikeStatus = rating == "INDIFFERENT" ? "" : rating;
                    if (currentTrack != null && TrackKey(currentTrack) == TrackKey(track)) currentTrack.LikeStatus = track.LikeStatus;
                    int index = resultsList.SelectedIndex;
                    if (index >= 0 && index < tracks.Count)
                    {
                        tracks[index].LikeStatus = track.LikeStatus;
                        resultsList.Items[index] = tracks[index];
                        resultsList.SelectedIndex = index;
                    }
                    string message = rating == "LIKE" ? "Música curtida." : rating == "DISLIKE" ? "Música descurtida." : "Avaliação removida.";
                    SetStatus(message);
                }));
            });
        }

        private void AddSelectedToPlaylist()
        {
            Track track = SelectedTrack();
            if (track == null) return;
            AddTrackToPlaylist(track);
        }

        private void AddTrackToPlaylist(Track track)
        {
            if (track == null) return;
            if (String.IsNullOrEmpty(track.VideoId))
            {
                SetStatus("Este item não pode ser adicionado a playlist.");
                return;
            }
            SetStatus("Carregando playlists.");
            RunWorker(delegate
            {
                var playlists = TracksFromBridge("playlists");
                BeginInvoke(new Action(delegate
                {
                    using (var form = new Form())
                    {
                        form.Text = "Adicionar a playlist";
                        form.Size = new Size(620, 420);
                        form.StartPosition = FormStartPosition.CenterParent;
                        var list = new ListBox();
                        list.Dock = DockStyle.Fill;
                        list.AccessibleName = "Lista de playlists";
                        foreach (var playlist in playlists) list.Items.Add(playlist);
                        form.Controls.Add(list);
                        var ok = new Button { Text = "Adicionar", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                        form.Controls.Add(ok);
                        form.AcceptButton = ok;
                        if (list.Items.Count > 0) list.SelectedIndex = 0;
                        if (form.ShowDialog(this) == DialogResult.OK && list.SelectedItem is Track)
                        {
                            Track playlist = (Track)list.SelectedItem;
                            RunWorker(delegate
                            {
                                RunBridge("add_to_playlist", playlist.PlaylistId, track.VideoId);
                                BeginInvoke(new Action(delegate { SetStatus("Música adicionada à playlist " + playlist.Title + "."); }));
                            });
                        }
                    }
                }));
            });
        }

        private void UpdateDependencies(bool quiet)
        {
            if (quiet && DependenciesAlreadyValidated())
            {
                SetStatus("Pronto. Dependências já verificadas.");
                return;
            }

            if (!quiet)
            {
                SetProgress(true);
                AnnounceStatus("Verificando atualizações das dependências.");
                ClearInstallAttempt("mpv");
                ClearInstallAttempt("node");
            }
            else
            {
                SetProgress(true);
                SetStatus("Verificando dependências.");
            }

            RunWorker(delegate
            {
                try
                {
                    BeginInvoke(new Action(delegate { SetProgressPercent(10, "Dependências"); }));
                    bool pythonReady = EnsurePythonRuntime(quiet);
                    bool ytdlpStandaloneReady = EnsureStandaloneYtdlp(quiet);
                    bool youtubeDlReady = EnsureYoutubeDl(quiet);
                    bool ytdlpStandaloneUpdated = quiet ? false : UpdateStandaloneYtdlp(quiet);
                    List<string> outdated = quiet ? new List<string>() : GetOutdatedDependencies();
                    BeginInvoke(new Action(delegate { SetProgressPercent(35, "Dependências"); }));
                    List<string> extraInstalled = EnsurePlaybackToolsInstalled();
                    BeginInvoke(new Action(delegate { SetProgressPercent(60, "Dependências"); }));
                    if (outdated.Count == 0 && !ytdlpStandaloneUpdated)
                    {
                        if (pythonReady && ytdlpStandaloneReady && youtubeDlReady && PortableRuntimeLooksComplete()) MarkDependenciesOk();
                        BeginInvoke(new Action(delegate
                        {
                            SetProgressPercent(100, "Dependências");
                            SetProgress(false);
                        if (pythonReady && ytdlpStandaloneReady && youtubeDlReady && extraInstalled.Count == 0 && !quiet) AnnounceStatus("Dependências em dia, incluindo Python, MPV, VLC, yt-dlp e youtube-dl.");
                            else if (extraInstalled.Count > 0) AnnounceStatus("Dependências preparadas: " + String.Join(", ", extraInstalled.ToArray()) + ".");
                            else if (!quiet) AnnounceStatus("Dependências em dia.");
                            else SetStatus("Pronto. Dependências verificadas.");
                        }));
                        return;
                    }

                    string names = String.Join(", ", outdated.ToArray());
                    if (outdated.Count > 0)
                    {
                        BeginInvoke(new Action(delegate
                        {
                            SetProgress(true);
                            AnnounceStatus("Atualizações disponíveis: " + names + ". Baixando e instalando agora.");
                        }));

                        RunProcess(GetPythonFileName(), "-m pip install --upgrade --force-reinstall pip setuptools ytmusicapi browser-cookie3 websocket-client python-vlc python-mpv", 180000);
                    }
                    if (pythonReady && ytdlpStandaloneReady && youtubeDlReady && PortableRuntimeLooksComplete()) MarkDependenciesOk();
                    BeginInvoke(new Action(delegate { SetProgressPercent(90, "Dependências"); }));

                    BeginInvoke(new Action(delegate
                    {
                        SetProgressPercent(100, "Dependências");
                        SetProgress(false);
                        if (extraInstalled.Count > 0) names += (String.IsNullOrWhiteSpace(names) ? "" : ", ") + String.Join(", ", extraInstalled.ToArray());
                        if (ytdlpStandaloneUpdated) names += (String.IsNullOrWhiteSpace(names) ? "" : ", ") + "yt-dlp standalone";
                        AnnounceStatus(String.IsNullOrWhiteSpace(names) ? "Dependências verificadas." : "Dependências atualizadas: " + names + ".");
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        SetStatus("Erro ao atualizar dependências: " + ex.Message);
                        if (!quiet) MessageBox.Show(ex.Message, Text);
                    }));
                }
            }, true);
        }

        private List<string> EnsurePlaybackToolsInstalled()
        {
            var installed = new List<string>();
            return installed;
        }

        private bool EnsurePythonRuntime(bool quiet)
        {
            string pythonDir = Path.Combine(runtimeDir, "Python");
            string pythonExe = Path.Combine(pythonDir, "python.exe");
            try
            {
                if (!File.Exists(pythonExe))
                {
                    Directory.CreateDirectory(runtimeDir);
                    Directory.CreateDirectory(pythonDir);
                    if (!quiet) AnnounceStatus("Baixando Python portátil.");
                    string zipPath = Path.Combine(runtimeDir, "python-3.12.10-embed-amd64.zip");
                    DownloadFileWithProgress(PythonEmbedUrl, zipPath, "Dependências");
                    string safeZip = zipPath.Replace("'", "''");
                    string safePythonDir = pythonDir.Replace("'", "''");
                    RunProcess("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '" + safeZip + "' -DestinationPath '" + safePythonDir + "' -Force\"", 180000);
                    try { File.Delete(zipPath); } catch { }
                    EnablePythonSiteImport(pythonDir);
                }
                else EnablePythonSiteImport(pythonDir);

                EnsurePipAndPythonPackages(pythonExe, quiet);
                return true;
            }
            catch (Exception ex)
            {
                if (!quiet) AnnounceStatus("Não consegui preparar o Python portátil: " + ShortError(ex.Message));
                return false;
            }
        }

        private void EnablePythonSiteImport(string pythonDir)
        {
            string pth = Directory.GetFiles(pythonDir, "python*._pth").FirstOrDefault();
            if (String.IsNullOrWhiteSpace(pth)) return;
            var lines = File.ReadAllLines(pth, Encoding.UTF8).ToList();
            bool hasImportSite = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("#") && lines[i].Trim().TrimStart('#').Trim() == "import site")
                    lines[i] = "import site";
                if (lines[i].Trim() == "import site") hasImportSite = true;
            }
            if (!hasImportSite) lines.Add("import site");
            File.WriteAllLines(pth, lines.ToArray(), new UTF8Encoding(false));
        }

        private void EnsurePipAndPythonPackages(string pythonExe, bool quiet)
        {
            bool pipReady = false;
            try
            {
                RunProcess(pythonExe, "-m pip --version", 60000, true);
                pipReady = true;
            }
            catch { }

            if (!pipReady)
            {
                string getPip = Path.Combine(runtimeDir, "get-pip.py");
                if (!quiet) AnnounceStatus("Preparando pip do Python portátil.");
                DownloadFileWithProgress(GetPipUrl, getPip, "Dependências");
                RunProcess(pythonExe, "\"" + EscapeArg(getPip) + "\"", 180000, true);
            }

            if (PythonPackagesReady(pythonExe)) return;
            RunProcess(pythonExe, "-m pip install pip setuptools ytmusicapi browser-cookie3 websocket-client python-vlc python-mpv", 180000, false);
        }

        private bool DependenciesAlreadyValidated()
        {
            try
            {
                if (!File.Exists(dependenciesOkFile)) return false;
                if (!File.Exists(GetStandaloneYtdlpPath())) return false;
                if (!File.Exists(GetYoutubeDlPath())) return false;
                string python = GetPythonFileName();
                if (String.IsNullOrWhiteSpace(python) || !File.Exists(python)) return false;
                return true;
            }
            catch { return false; }
        }

        private void MarkDependenciesOk()
        {
            try { File.WriteAllText(dependenciesOkFile, "ok|" + DateTime.Now.ToString("s"), Encoding.UTF8); }
            catch { }
        }

        private bool PythonPackagesReady(string pythonExe)
        {
            try
            {
                RunProcess(pythonExe, "-c \"import pip, ytmusicapi, browser_cookie3, websocket, vlc, mpv; print('ok')\"", 60000, true);
                return true;
            }
            catch { return false; }
        }

        private bool EnsureStandaloneYtdlp(bool quiet)
        {
            string target = GetStandaloneYtdlpPath();
            try
            {
                if (File.Exists(target))
                {
                    RunProcess(target, "--version", 30000, true);
                    return true;
                }
            }
            catch
            {
                try { File.Delete(target); } catch { }
            }

            try
            {
                Directory.CreateDirectory(runtimeDir);
                if (!quiet) AnnounceStatus("Baixando yt-dlp portátil.");
                DownloadFileWithProgress(StandaloneYtdlpUrl, target, "Dependências");
                RunProcess(target, "--version", 30000, true);
                return true;
            }
            catch (Exception ex)
            {
                if (!quiet) AnnounceStatus("Não consegui baixar yt-dlp portátil: " + ShortError(ex.Message));
                return false;
            }
        }

        private bool EnsureYoutubeDl(bool quiet)
        {
            string target = GetYoutubeDlPath();
            try
            {
                if (File.Exists(target))
                {
                    RunProcess(target, "--version", 30000, true);
                    return true;
                }
            }
            catch
            {
                try { File.Delete(target); } catch { }
            }

            try
            {
                Directory.CreateDirectory(runtimeDir);
                if (!quiet) AnnounceStatus("Baixando youtube-dl portátil.");
                DownloadFileWithProgress(YoutubeDlExeUrl, target, "Dependências");
                RunProcess(target, "--version", 30000, true);
                return true;
            }
            catch (Exception ex)
            {
                if (!quiet) AnnounceStatus("Não consegui baixar youtube-dl portátil: " + ShortError(ex.Message));
                return false;
            }
        }

        private bool UpdateStandaloneYtdlp(bool quiet)
        {
            string target = GetStandaloneYtdlpPath();
            if (!File.Exists(target)) return false;
            try
            {
                string before = RunProcess(target, "--version", 30000, true).Trim();
                if (!quiet) SetStatus("Verificando atualização do yt-dlp.");
                RunProcess(target, "-U", 180000, false);
                string after = RunProcess(target, "--version", 30000, true).Trim();
                return !String.Equals(before, after, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                try { File.Delete(target); } catch { }
                return EnsureStandaloneYtdlp(quiet);
            }
        }

        private void RepairStandaloneYtdlp()
        {
            string target = GetStandaloneYtdlpPath();
            Directory.CreateDirectory(runtimeDir);
            try
            {
                if (File.Exists(target)) File.Delete(target);
            }
            catch { }
            DownloadFileWithProgress(StandaloneYtdlpUrl, target, "yt-dlp");
            RunProcess(target, "--version", 30000, true);
            try
            {
                DeleteIfExists(dependenciesOkFile);
            }
            catch { }
        }

        private bool WasInstallAttempted(string name)
        {
            return File.Exists(Path.Combine(configDir, name + "_install_attempted.flag"));
        }

        private void MarkInstallAttempted(string name)
        {
            try { File.WriteAllText(Path.Combine(configDir, name + "_install_attempted.flag"), DateTime.Now.ToString("s"), Encoding.UTF8); }
            catch { }
        }

        private void ClearInstallAttempt(string name)
        {
            try
            {
                string path = Path.Combine(configDir, name + "_install_attempted.flag");
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private void BeginDelayedAppUpdateCheck()
        {
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 7000;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                CheckAppUpdate(true);
            };
            timer.Start();
        }

        private void AnnouncePendingUpdateNotes()
        {
            try
            {
                if (!File.Exists(pendingUpdateNotesFile)) return;
                string notes = File.ReadAllText(pendingUpdateNotesFile, Encoding.UTF8).Trim();
                DeleteIfExists(pendingUpdateNotesFile);
                if (String.IsNullOrWhiteSpace(notes)) return;
                AnnounceStatus(notes);
                MessageBox.Show(notes, Text);
            }
            catch { }
        }

        private void CheckAppUpdate(bool quiet)
        {
            if (quiet && !ShouldCheckForUpdatesAutomatically())
                return;
            if (!quiet)
            {
                SetProgress(true);
                AnnounceStatus("Verificando atualização do aplicativo.");
            }

            RunWorker(delegate
            {
                try
                {
                    MarkUpdateCheckNow();
                    GithubReleaseUpdate update = DownloadGitHubReleaseUpdate();

                    if (!IsNewerVersion(update.Version, AppVersion))
                    {
                        BeginInvoke(new Action(delegate
                        {
                            SetProgress(false);
                            if (!quiet) AnnounceStatus("Você já está usando a versão mais recente.");
                        }));
                        return;
                    }
                    if (quiet && IsIgnoredUpdate(update.Version)) return;

                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Atualização disponível. Versão " + update.Version + ".");
                        string choice = ShowUpdateDialog(update);
                        if (choice == "update")
                        {
                            DownloadAndApplyAppUpdate(update.Version, update.ZipUrl, update.ShaUrl, update.Notes);
                        }
                        else if (choice == "ignore") IgnoreUpdate(update.Version);
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        if (!quiet) AnnounceStatus("Não consegui verificar atualização: " + ShortError(ex.Message));
                    }));
                }
            }, true);
        }

        private bool ShouldCheckForUpdatesAutomatically()
        {
            try
            {
                string path = Path.Combine(configDir, LastUpdateCheckFileName);
                if (!File.Exists(path)) return true;
                DateTime last;
                if (!DateTime.TryParse(File.ReadAllText(path, Encoding.UTF8).Trim(), out last)) return true;
                return (DateTime.Now - last).TotalHours >= 22;
            }
            catch { return true; }
        }

        private void MarkUpdateCheckNow()
        {
            try { File.WriteAllText(Path.Combine(configDir, LastUpdateCheckFileName), DateTime.Now.ToString("o"), Encoding.UTF8); }
            catch { }
        }

        private bool IsIgnoredUpdate(string version)
        {
            try
            {
                string path = Path.Combine(configDir, IgnoredUpdateFileName);
                return File.Exists(path) && File.ReadAllText(path, Encoding.UTF8).Trim().Equals(version, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void IgnoreUpdate(string version)
        {
            try { File.WriteAllText(Path.Combine(configDir, IgnoredUpdateFileName), version ?? "", Encoding.UTF8); }
            catch { }
            AnnounceStatus("Atualização ignorada.");
        }

        private string ShowUpdateDialog(GithubReleaseUpdate update)
        {
            using (var form = new Form())
            {
                form.Text = "Nova versão disponível";
                form.Size = new Size(620, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);
                var text = new TextBox();
                text.Multiline = true;
                text.ReadOnly = true;
                text.ScrollBars = ScrollBars.Vertical;
                text.Dock = DockStyle.Fill;
                text.AccessibleName = "Informações da atualização";
                text.Text = "Versão instalada: " + AppVersion + "\r\nNova versão: " + update.Version + "\r\n\r\nNovidades:\r\n" + (String.IsNullOrWhiteSpace(update.Notes) ? "Sem changelog informado." : update.Notes);
                panel.Controls.Add(text, 0, 0);
                var buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = FlowDirection.LeftToRight;
                panel.Controls.Add(buttons, 0, 1);
                string result = "close";
                Button updateButton = new Button { Text = "Atualizar agora", AutoSize = true };
                Button laterButton = new Button { Text = "Lembrar depois", AutoSize = true };
                Button ignoreButton = new Button { Text = "Ignorar esta versão", AutoSize = true };
                Button closeButton = new Button { Text = "Fechar", AutoSize = true };
                buttons.Controls.Add(updateButton);
                buttons.Controls.Add(laterButton);
                buttons.Controls.Add(ignoreButton);
                buttons.Controls.Add(closeButton);
                updateButton.Click += delegate { result = "update"; form.Close(); };
                laterButton.Click += delegate { result = "later"; form.Close(); };
                ignoreButton.Click += delegate { result = "ignore"; form.Close(); };
                closeButton.Click += delegate { result = "close"; form.Close(); };
                form.AcceptButton = updateButton;
                form.CancelButton = closeButton;
                form.ShowDialog(this);
                return result;
            }
        }

        private WebClient NewWebClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new WebClient();
            client.Encoding = Encoding.UTF8;
            client.Headers.Add("User-Agent", "Youtube-Light/" + AppVersion);
            return client;
        }

        private GithubReleaseUpdate DownloadGitHubReleaseUpdate()
        {
            using (var client = NewWebClient())
            {
                client.Headers["Accept"] = "application/vnd.github+json";
                string json = client.DownloadString(GitHubLatestReleaseApiUrl);
                var release = serializer.Deserialize<Dictionary<string, object>>(json);
                if (release == null) throw new Exception("Resposta inválida do GitHub Releases.");
                string tag = GetString(release, "tag_name", "");
                string body = GetString(release, "body", "");
                string releaseVersion = NormalizeReleaseVersion(tag);
                object assetsObj;
                if (!release.TryGetValue("assets", out assetsObj)) throw new Exception("Release sem assets.");
                var assets = assetsObj as IEnumerable;
                if (assets == null) throw new Exception("Release sem assets.");

                Dictionary<string, object> zipAsset = null;
                Dictionary<string, object> shaAsset = null;
                foreach (object obj in assets)
                {
                    var asset = obj as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = GetString(asset, "name", "");
                    if (Regex.IsMatch(name, UpdateAssetPattern, RegexOptions.IgnoreCase))
                    {
                        zipAsset = asset;
                        Match match = Regex.Match(name, UpdateAssetPattern, RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups["version"].Success)
                            releaseVersion = match.Groups["version"].Value;
                    }
                    else if (name.EndsWith(UpdateShaAssetSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        shaAsset = asset;
                    }
                }

                if (zipAsset == null) throw new Exception("Não encontrei o ZIP portátil da Release.");
                if (shaAsset == null) throw new Exception("Não encontrei o arquivo SHA-256 da Release.");
                string zipUrl = GetString(zipAsset, "browser_download_url", "");
                string shaUrl = GetString(shaAsset, "browser_download_url", "");
                if (String.IsNullOrWhiteSpace(releaseVersion) || String.IsNullOrWhiteSpace(zipUrl) || String.IsNullOrWhiteSpace(shaUrl))
                    throw new Exception("Release com metadados incompletos.");
                if (!IsTrustedGitHubAssetUrl(zipUrl) || !IsTrustedGitHubAssetUrl(shaUrl))
                    throw new Exception("Release contém URL de asset inesperada.");
                return new GithubReleaseUpdate { Version = releaseVersion, ZipUrl = zipUrl, ShaUrl = shaUrl, Notes = body };
            }
        }

        private string NormalizeReleaseVersion(string tag)
        {
            tag = (tag ?? "").Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
            return tag;
        }

        private bool IsTrustedGitHubAssetUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            string host = uri.Host.ToLowerInvariant();
            return host == "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsNewerVersion(string latest, string current)
        {
            try
            {
                return new Version(latest) > new Version(current);
            }
            catch
            {
                return !String.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string BuildUpdateMessage(string version, string notes)
        {
            string message = "Atualização concluída. Versão " + version + ".";
            if (!String.IsNullOrWhiteSpace(notes))
                message += " O que mudou: " + notes.Trim();
            return message;
        }

        private void DownloadAndApplyAppUpdate(string version, string url, string shaUrl, string notes)
        {
            SetProgress(true);
            AnnounceStatus("Baixando atualização " + version + ".");
            RunWorker(delegate
            {
                try
                {
                    string tempRoot = Path.Combine(Path.GetTempPath(), "Youtube_Light_Update_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempRoot);
                    string zipPath = Path.Combine(tempRoot, "update.zip");
                    DownloadFileWithProgress(url, zipPath, "Atualização");
                    string shaPath = Path.Combine(tempRoot, "update.zip.sha256");
                    DownloadFileWithProgress(shaUrl, shaPath, "Atualização");
                    string expectedHash = ReadExpectedSha256(shaPath);
                    string actualHash = CalculateSha256(zipPath);
                    if (!String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(zipPath); } catch { }
                        throw new Exception("A atualização foi baixada, mas não passou na verificação de integridade. Nenhum arquivo foi alterado.");
                    }

                    string updateMessage = BuildUpdateMessage(version, notes);
                    File.WriteAllText(pendingUpdateNotesFile, updateMessage, Encoding.UTF8);

                    string scriptPath = Path.Combine(tempRoot, "aplicar_atualizacao.bat");
                    string psScriptPath = Path.Combine(tempRoot, "aplicar_atualizacao.ps1");
                    string exePath = Path.Combine(baseDir, "YoutubeMusicLightAccessible.exe");
                    int currentPid = Process.GetCurrentProcess().Id;
                    string safeZipPath = zipPath.Replace("'", "''");
                    string safeBaseDir = baseDir.Replace("'", "''");
                    string safeConfigDir = configDir.Replace("'", "''");
                    string safeLocalDataDir = localDataDir.Replace("'", "''");
                    string psUpdate =
                        "$ErrorActionPreference='Stop'\r\n" +
                        "$zip='" + safeZipPath + "'\r\n" +
                        "$base='" + safeBaseDir + "'\r\n" +
                        "$configDir='" + safeConfigDir + "'\r\n" +
                        "$localDataDir='" + safeLocalDataDir + "'\r\n" +
                        "$pidToWait=" + currentPid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                        "$extract=Join-Path $env:TEMP ('Youtube_Light_Extract_' + [guid]::NewGuid().ToString('N'))\r\n" +
                        "$backupRoot=Join-Path $localDataDir 'updates\\backups'\r\n" +
                        "$backup=Join-Path $backupRoot ('" + AppVersion.Replace("'", "''") + "_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))\r\n" +
                        "$log=Join-Path $localDataDir 'logs\\ultima_atualizacao.log'\r\n" +
                        "New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null\r\n" +
                        "New-Item -ItemType Directory -Path $backup -Force | Out-Null\r\n" +
                        "'Iniciando atualização ' + (Get-Date) | Set-Content -LiteralPath $log -Encoding UTF8\r\n" +
                        "for($i=0; $i -lt 120; $i++){ if(-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)){ break }; Start-Sleep -Milliseconds 500 }\r\n" +
                        "if(Get-Process -Id $pidToWait -ErrorAction SilentlyContinue){ throw 'O aplicativo antigo não fechou a tempo.' }\r\n" +
                        "New-Item -ItemType Directory -Path $extract -Force | Out-Null\r\n" +
                        "Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force\r\n" +
                        "$items=@(Get-ChildItem -LiteralPath $extract -Force)\r\n" +
                        "if($items.Count -eq 1 -and $items[0].PSIsContainer){ $source=$items[0].FullName } else { $source=$extract }\r\n" +
                        "$distributed=@('YoutubeMusicLightAccessible.exe','librarys','licenses','LEIA-ME.txt','Tutorial Youtube-Music-Light.txt','THIRD_PARTY_LICENSES.txt')\r\n" +
                        "foreach($name in $distributed){ $old=Join-Path $base $name; if(Test-Path -LiteralPath $old){ Move-Item -LiteralPath $old -Destination (Join-Path $backup $name) -Force } }\r\n" +
                        "$robocopyArgs=@($source,$base,'/E','/R:10','/W:1','/NFL','/NDL','/NJH','/NJS','/NP')\r\n" +
                        "& robocopy @robocopyArgs | Out-File -LiteralPath $log -Encoding UTF8 -Append\r\n" +
                        "$code=$LASTEXITCODE\r\n" +
                        "if($code -gt 7){ foreach($name in $distributed){ $old=Join-Path $backup $name; if(Test-Path -LiteralPath $old){ Copy-Item -LiteralPath $old -Destination (Join-Path $base $name) -Recurse -Force } }; throw 'Falha ao copiar atualização. Código do robocopy: ' + $code }\r\n" +
                        "$versionFile=Join-Path $configDir 'versao_local.dat'\r\n" +
                        "New-Item -ItemType Directory -Path $configDir -Force | Out-Null\r\n" +
                        "'" + version.Replace("'", "''") + "' | Set-Content -LiteralPath $versionFile -Encoding UTF8\r\n" +
                        "Get-ChildItem -LiteralPath $backupRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -Skip 5 | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                        "Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                        "Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue\r\n" +
                        "'Atualização aplicada com sucesso ' + (Get-Date) | Add-Content -LiteralPath $log -Encoding UTF8\r\n";
                    string script =
                        "@echo off\r\n" +
                        "cd /d \"" + baseDir + "\"\r\n" +
                        "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + psScriptPath + "\"\r\n" +
                        "if errorlevel 1 exit /b 1\r\n" +
                        "start \"\" \"" + exePath + "\"\r\n" +
                        "del \"%~f0\"\r\n";
                    File.WriteAllText(psScriptPath, psUpdate, new UTF8Encoding(false));
                    File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Atualização baixada. O aplicativo vai fechar e abrir novamente.");
                        StartProcessNoWindow("cmd.exe", "/c \"" + scriptPath + "\"");
                        Close();
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Erro ao baixar atualização: " + ShortError(ex.Message));
                    }));
                }
            }, true);
        }

        private string ReadExpectedSha256(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8).Trim();
            Match match = Regex.Match(text, "[a-fA-F0-9]{64}");
            if (!match.Success) throw new Exception("Arquivo SHA-256 inválido.");
            return match.Value.ToLowerInvariant();
        }

        private string CalculateSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder();
                foreach (byte b in hash) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        private void DownloadAndApplyExeUpdate(string version, string url, string notes)
        {
            SetProgress(true);
            AnnounceStatus("Baixando atualização leve " + version + ".");
            RunWorker(delegate
            {
                try
                {
                    string tempRoot = Path.Combine(Path.GetTempPath(), "Youtube_Light_Exe_Update_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempRoot);
                    string newExe = Path.Combine(tempRoot, "YoutubeMusicLightAccessible.exe");
                    DownloadFileWithProgress(url, newExe, "Atualização");

                    string updateMessage = BuildUpdateMessage(version, notes);
                    File.WriteAllText(pendingUpdateNotesFile, updateMessage, Encoding.UTF8);

                    string scriptPath = Path.Combine(tempRoot, "aplicar_atualizacao_exe.bat");
                    string exePath = Path.Combine(baseDir, "YoutubeMusicLightAccessible.exe");
                    string backupPath = Path.Combine(tempRoot, "YoutubeMusicLightAccessible.antigo.exe");
                    string script =
                        "@echo off\r\n" +
                        "cd /d \"" + baseDir + "\"\r\n" +
                        "timeout /t 2 /nobreak >nul\r\n" +
                        "if exist \"" + backupPath + "\" del \"" + backupPath + "\"\r\n" +
                        "if exist \"" + exePath + "\" move /Y \"" + exePath + "\" \"" + backupPath + "\" >nul\r\n" +
                        "move /Y \"" + newExe + "\" \"" + exePath + "\" >nul\r\n" +
                        "start \"\" \"" + exePath + "\"\r\n" +
                        "del \"%~f0\"\r\n";
                    File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Atualização leve baixada. O aplicativo vai fechar e abrir novamente.");
                        StartProcessNoWindow("cmd.exe", "/c \"" + scriptPath + "\"");
                        Close();
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        SetProgress(false);
                        AnnounceStatus("Erro ao baixar atualização leve: " + ShortError(ex.Message));
                    }));
                }
            }, true);
        }

        private void DownloadFileWithProgress(string url, string target, string label)
        {
            using (var client = NewWebClient())
            {
                Exception failure = null;
                using (var done = new ManualResetEvent(false))
                {
                    client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                    {
                        SetProgressPercent(e.ProgressPercentage, label);
                    };
                    client.DownloadFileCompleted += delegate(object sender, AsyncCompletedEventArgs e)
                    {
                        if (e.Error != null) failure = e.Error;
                        done.Set();
                    };
                    client.DownloadFileAsync(new Uri(url), target);
                    done.WaitOne();
                    if (failure != null) throw failure;
                    SetProgressPercent(100, label);
                }
            }
        }

        private List<string> GetOutdatedDependencies()
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            wanted.Add("ytmusicapi");
            wanted.Add("browser-cookie3");
            wanted.Add("websocket-client");
            wanted.Add("python-vlc");
            wanted.Add("python-mpv");
            wanted.Add("pip");
            wanted.Add("setuptools");

            var result = new List<string>();
            string python = GetPythonFileName();
            if (String.IsNullOrWhiteSpace(python)) return result;
            string output = RunProcess(python, "-m pip list --outdated --format=json", 60000, false).Trim();
            output = ExtractJsonArray(output);
            if (String.IsNullOrWhiteSpace(output)) return result;

            var items = serializer.Deserialize<List<Dictionary<string, object>>>(output);
            foreach (var item in items)
            {
                string name = GetString(item, "name", "");
                if (wanted.Contains(name)) result.Add(name);
            }
            return result;
        }

        private string ExtractJsonArray(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";
            int start = text.IndexOf('[');
            int end = text.LastIndexOf(']');
            if (start < 0 || end < start) return "";
            return text.Substring(start, end - start + 1);
        }

        private void ChooseMpv()
        {
            SetStatus("O player interno é o principal. O MPV fica apenas como reserva automática quando disponível.");
        }

        private void ShowSettings()
        {
            using (var form = new Form())
            {
                form.Text = "Configurações";
                form.Size = new Size(640, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Lista de configurações";
                form.Controls.Add(list);
                Action refresh = delegate
                {
                    list.Items.Clear();
                    list.Items.Add("Anúncios automáticos do player: " + (announcePlayerEvents ? "ligados" : "desligados"));
                    list.Items.Add("Rádio infinita: " + (infiniteRadio ? "ligada" : "desligada"));
                    list.Items.Add("Reprodução automática: " + (autoplayEnabled ? "ligada" : "desligada"));
                    list.Items.Add("Normalização simples de volume: " + (normalizeVolume ? "ligada" : "desligada"));
                    list.Items.Add("Modo padrão de busca: " + (musicOnlyMode ? "YouTube Music" : "YouTube completo"));
                    list.Items.Add("Áudio e transmissão");
                    list.Items.Add("Tocar por áudio temporário: " + (preferTemporaryAudio ? "ligado" : "desligado"));
                    list.Items.Add("Notificações de vídeos novos: " + (realtimeVideoNotifications ? "ligadas" : "desligadas"));
                    list.Items.Add("Ler notificações automaticamente: " + (autoReadVideoNotifications ? "sim" : "não"));
                    list.Items.Add("Intervalo das notificações: " + notificationIntervalMinutes + " minutos");
                    list.Items.Add("Verificar vídeos novos agora");
                    list.Items.Add("Volume boost máximo: " + volumeBoostPercent + " por cento");
                    list.Items.Add("Segundos do Alt Shift setas: " + altShiftSeekSeconds);
                    list.Items.Add("Atalhos personalizados");
                    list.Items.Add("Limpar fila de reprodução");
                    list.Items.Add("Limpar histórico local");
                    list.Items.Add("Fechar configurações");
                    list.SelectedIndex = 0;
                };
                refresh();
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter || list.SelectedItem == null) return;
                    e.SuppressKeyPress = true;
                    string item = list.SelectedItem.ToString();
                    if (item.StartsWith("Anúncios")) announcePlayerEvents = !announcePlayerEvents;
                    else if (item.StartsWith("Rádio")) infiniteRadio = !infiniteRadio;
                    else if (item.StartsWith("Reprodução automática")) autoplayEnabled = !autoplayEnabled;
                    else if (item.StartsWith("Normalização")) normalizeVolume = !normalizeVolume;
                    else if (item.StartsWith("Modo padrão")) ToggleMusicOnlyMode();
                    else if (item == "Áudio e transmissão") ShowAudioSettings();
                    else if (item.StartsWith("Tocar por áudio temporário")) preferTemporaryAudio = !preferTemporaryAudio;
                    else if (item.StartsWith("Notificações de vídeos novos")) { realtimeVideoNotifications = !realtimeVideoNotifications; RestartNotificationTimer(); }
                    else if (item.StartsWith("Ler notificações automaticamente")) autoReadVideoNotifications = !autoReadVideoNotifications;
                    else if (item.StartsWith("Intervalo das notificações")) ConfigureNotificationInterval();
                    else if (item == "Verificar vídeos novos agora") { CheckNewSubscriptionVideos(true); return; }
                    else if (item.StartsWith("Volume boost")) ConfigureVolumeBoost();
                    else if (item.StartsWith("Segundos do Alt Shift")) ConfigureAltShiftSeekSeconds();
                    else if (item == "Atalhos personalizados") ShowShortcutSettings();
                    else if (item == "Limpar fila de reprodução") { playbackQueue.Clear(); SaveLocalData(); SetStatus("Fila limpa."); }
                    else if (item == "Limpar histórico local") { localHistory.Clear(); SaveLocalData(); SetStatus("Histórico local limpo."); }
                    else if (item == "Fechar configurações") { form.Close(); return; }
                    SaveConfig();
                    if (trayIcon != null) trayIcon.Visible = false;
                    refresh();
                    Speak("Configuração alterada.");
                };
                form.ShowDialog(this);
            }
        }

        private void ShowShortcutSettings()
        {
            using (var form = new Form())
            {
                form.Text = "Atalhos personalizados";
                form.Size = new Size(720, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Lista de atalhos personalizados";
                form.Controls.Add(list);
                Action refresh = delegate
                {
                    list.Items.Clear();
                    list.Items.Add("Busca: " + customShortcuts["search"]);
                    list.Items.Add("Pausar ou retomar: " + customShortcuts["pause"]);
                    list.Items.Add("Voltar segundos: " + customShortcuts["seekBack"]);
                    list.Items.Add("Avançar segundos: " + customShortcuts["seekForward"]);
                    list.Items.Add("Aumentar volume: " + customShortcuts["volumeUp"]);
                    list.Items.Add("Diminuir volume: " + customShortcuts["volumeDown"]);
                    list.Items.Add("Volume: " + customShortcuts["volume"]);
                    list.Items.Add("Tempo: " + customShortcuts["time"]);
                    list.Items.Add("Link: " + customShortcuts["link"]);
                    list.Items.Add("Próxima música: " + customShortcuts["next"]);
                    list.Items.Add("Música anterior: " + customShortcuts["previous"]);
                    list.Items.Add("Restaurar atalhos padrão");
                    list.Items.Add("Fechar atalhos");
                    list.SelectedIndex = 0;
                };
                refresh();
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter || list.SelectedItem == null) return;
                    e.SuppressKeyPress = true;
                    string action = ShortcutActionFromIndex(list.SelectedIndex);
                    if (action == "defaults")
                    {
                        customShortcuts.Clear();
                        ApplyDefaultShortcuts();
                        SaveConfig();
                        refresh();
                        Speak("Atalhos padrão restaurados.");
                        return;
                    }
                    if (action == "close") { form.Close(); return; }
                    Keys captured = CaptureShortcut(form);
                    if (captured != Keys.None)
                    {
                        customShortcuts[action] = captured;
                        SaveConfig();
                        refresh();
                        Speak("Atalho salvo.");
                    }
                };
                form.ShowDialog(this);
            }
        }

        private void ConfigureAltShiftSeekSeconds()
        {
            using (var form = new Form())
            {
                form.Text = "Segundos do Alt Shift setas";
                form.Size = new Size(420, 170);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);

                var label = new Label();
                label.Text = "Digite quantos segundos quer avançar ou voltar.";
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);

                var box = new TextBox();
                box.Text = altShiftSeekSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                box.AccessibleName = "Segundos do atalho Alt Shift setas";
                panel.Controls.Add(box, 0, 1);

                var ok = new Button { Text = "Salvar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;

                if (form.ShowDialog(this) != DialogResult.OK) return;
                int seconds;
                if (!Int32.TryParse(box.Text.Trim(), out seconds))
                {
                    AnnounceStatus("Valor inválido.");
                    return;
                }
                altShiftSeekSeconds = Math.Max(1, Math.Min(300, seconds));
                SaveConfig();
                AnnounceStatus("Atalho Alt Shift setas definido para " + altShiftSeekSeconds + " segundos.");
            }
        }

        private void ConfigureNotificationInterval()
        {
            using (var form = new Form())
            {
                form.Text = "Intervalo das notificações";
                form.Size = new Size(440, 170);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);

                var label = new Label();
                label.Text = "Digite o intervalo em minutos para verificar vídeos novos.";
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);

                var box = new TextBox();
                box.Text = notificationIntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
                box.AccessibleName = "Intervalo das notificações em minutos";
                panel.Controls.Add(box, 0, 1);

                var ok = new Button { Text = "Salvar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;

                if (form.ShowDialog(this) != DialogResult.OK) return;
                int minutes;
                if (!Int32.TryParse(box.Text.Trim(), out minutes))
                {
                    AnnounceStatus("Valor inválido.");
                    return;
                }
                notificationIntervalMinutes = Math.Max(5, Math.Min(1440, minutes));
                SaveConfig();
                RestartNotificationTimer();
                AnnounceStatus("Notificações definidas para cada " + notificationIntervalMinutes + " minutos.");
            }
        }

        private void ConfigureVolumeBoost()
        {
            using (var form = new Form())
            {
                form.Text = "Volume boost";
                form.Size = new Size(440, 170);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);

                var label = new Label();
                label.Text = "Digite o volume máximo. Use 100 para normal, até 200 para boost.";
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);

                var box = new TextBox();
                box.Text = volumeBoostPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
                box.AccessibleName = "Volume máximo com boost";
                panel.Controls.Add(box, 0, 1);

                var ok = new Button { Text = "Salvar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;

                if (form.ShowDialog(this) != DialogResult.OK) return;
                int percent;
                if (!Int32.TryParse(box.Text.Trim(), out percent))
                {
                    AnnounceStatus("Valor inválido.");
                    return;
                }
                volumeBoostPercent = Math.Max(100, Math.Min(200, percent));
                savedVolume = ClampAppVolume(savedVolume);
                SaveConfig();
                AnnounceStatus("Volume boost máximo definido para " + volumeBoostPercent + " por cento.");
            }
        }

        private void ShowAudioSettings()
        {
            using (var form = new Form())
            {
                form.Text = "Áudio e transmissão";
                form.Size = new Size(720, 460);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Configurações de áudio e transmissão";
                list.AccessibleDescription = "Use as setas e pressione Enter.";
                form.Controls.Add(list);
                Action refresh = delegate
                {
                    list.Items.Clear();
                    list.Items.Add("Saída principal ou transmissão: " + (String.IsNullOrWhiteSpace(selectedOutputDeviceName) ? "automático do Windows" : CleanDeviceName(selectedOutputDeviceName)));
                    list.Items.Add("Retorno do player no fone: " + (playerMonitorEnabled ? "ligado" : "desligado"));
                    list.Items.Add("Dispositivo do retorno do player: " + (String.IsNullOrWhiteSpace(selectedMonitorOutputDeviceName) ? "nenhum selecionado" : CleanDeviceName(selectedMonitorOutputDeviceName)));
                    list.Items.Add("Volume do retorno do player: " + playerMonitorVolume + " por cento");
                    list.Items.Add("Dispositivo de entrada: " + (String.IsNullOrWhiteSpace(selectedInputDeviceName) ? "nenhum microfone selecionado" : selectedInputDeviceName));
                    list.Items.Add("Saída da captura do microfone: " + (String.IsNullOrWhiteSpace(selectedMicOutputDeviceName) ? "automático do Windows" : CleanDeviceName(selectedMicOutputDeviceName)));
                    list.Items.Add("Microfone no player: " + (micMonitorEnabled ? "ligado" : "desligado"));
                    list.Items.Add("Microfone mutado: " + (micMuted ? "sim" : "não"));
                    list.Items.Add("Volume do microfone: " + micVolume + " por cento");
                    list.Items.Add("Modo de escuta: " + AudioListenModeLabel());
                    list.Items.Add("Fechar áudio e transmissão");
                    list.SelectedIndex = 0;
                };
                refresh();
                list.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter || list.SelectedItem == null) return;
                    e.SuppressKeyPress = true;
                    string item = list.SelectedItem.ToString();
                    if (item.StartsWith("Saída principal")) ChooseOutputDevice();
                    else if (item.StartsWith("Retorno do player")) TogglePlayerMonitor();
                    else if (item.StartsWith("Dispositivo do retorno")) ChooseMonitorOutputDevice();
                    else if (item.StartsWith("Volume do retorno")) ConfigurePlayerMonitorVolume();
                    else if (item.StartsWith("Dispositivo de entrada")) ChooseInputDevice();
                    else if (item.StartsWith("Saída da captura")) ChooseMicOutputDevice();
                    else if (item.StartsWith("Microfone no player")) ToggleMicMonitor();
                    else if (item.StartsWith("Microfone mutado")) ToggleMicMute();
                    else if (item.StartsWith("Volume do microfone")) ConfigureMicVolume();
                    else if (item.StartsWith("Modo de escuta")) ChooseAudioListenMode();
                    else if (item == "Fechar áudio e transmissão") { form.Close(); return; }
                    refresh();
                };
                form.ShowDialog(this);
            }
        }

        private string AudioListenModeLabel()
        {
            if (audioListenMode == "microphone") return "somente microfone";
            if (audioListenMode == "both") return "vídeo e microfone";
            return "microfone desligado";
        }

        private string CleanDeviceName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "";
            return name.Replace("Padr�o", "automático do Windows").Replace("padr�o", "automático do Windows").Replace("Padrão", "automático do Windows").Replace("padrão", "automático do Windows");
        }

        private void TogglePlayerMonitor()
        {
            playerMonitorEnabled = !playerMonitorEnabled;
            SaveConfig();
            if (playerMonitorEnabled) RestartPlayerMonitor();
            else StopPlayerMonitor();
            AnnounceStatus(playerMonitorEnabled ? "Retorno do player ligado." : "Retorno do player desligado.");
        }

        private void ChooseMonitorOutputDevice()
        {
            if (!usingVlc && !usingMpv)
            {
                AnnounceStatus("Para escolher o fone de retorno, toque um vídeo primeiro.");
                return;
            }
            List<AudioDevice> devices = GetActiveAudioDevices();
            if (devices.Count == 0)
            {
                AnnounceStatus("Não encontrei dispositivos de saída no player.");
                return;
            }
            using (var form = new Form())
            {
                form.Text = "Dispositivo do retorno do player";
                form.Size = new Size(620, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Dispositivo do retorno do player";
                list.AccessibleDescription = "Escolha o fone ou alto-falante onde você quer ouvir o retorno.";
                foreach (AudioDevice device in devices) list.Items.Add(device);
                int selectedIndex = devices.FindIndex(d => String.Equals(d.Id, selectedMonitorOutputDeviceId, StringComparison.OrdinalIgnoreCase));
                list.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Usar este dispositivo", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK || !(list.SelectedItem is AudioDevice)) return;
                AudioDevice selected = (AudioDevice)list.SelectedItem;
                selectedMonitorOutputDeviceId = selected.Id;
                selectedMonitorOutputDeviceName = selected.ToString();
                SaveConfig();
                RestartPlayerMonitor();
                AnnounceStatus("Retorno do player definido para " + selected + ".");
            }
        }

        private void ConfigurePlayerMonitorVolume()
        {
            using (var form = new Form())
            {
                form.Text = "Volume do retorno do player";
                form.Size = new Size(440, 170);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);
                var label = new Label { Text = "Digite o volume do retorno, de 0 a 200.", AutoSize = true };
                panel.Controls.Add(label, 0, 0);
                var box = new TextBox { Text = playerMonitorVolume.ToString(System.Globalization.CultureInfo.InvariantCulture), AccessibleName = "Volume do retorno do player" };
                panel.Controls.Add(box, 0, 1);
                var ok = new Button { Text = "Salvar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                int value;
                if (!Int32.TryParse(box.Text.Trim(), out value)) { AnnounceStatus("Valor inválido."); return; }
                playerMonitorVolume = Math.Max(0, Math.Min(200, value));
                SaveConfig();
                SendPlayerMonitorCommand("{\"command\":\"set-volume\",\"volume\":" + playerMonitorVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", true);
                AnnounceStatus("Volume do retorno definido para " + playerMonitorVolume + " por cento.");
            }
        }

        private void ChooseInputDevice()
        {
            List<string> microphones = ListInputDevices();
            if (microphones.Count == 0)
            {
                AnnounceStatus("Não encontrei microfones pelo FFmpeg.");
                return;
            }
            using (var form = new Form())
            {
                form.Text = "Escolher microfone";
                form.Size = new Size(640, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Lista de microfones";
                foreach (string mic in microphones) list.Items.Add(mic);
                int selected = microphones.FindIndex(m => String.Equals(m, selectedInputDeviceName, StringComparison.OrdinalIgnoreCase));
                list.SelectedIndex = selected >= 0 ? selected : 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Usar este microfone", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK || list.SelectedItem == null) return;
                selectedInputDeviceName = list.SelectedItem.ToString();
                SaveConfig();
                if (micMonitorEnabled) RestartMicMonitor();
                AnnounceStatus("Microfone selecionado: " + selectedInputDeviceName + ".");
            }
        }

        private void ChooseMicOutputDevice()
        {
            List<AudioDevice> devices;
            try
            {
                devices = GetActiveAudioDevices();
            }
            catch (Exception ex)
            {
                AnnounceStatus("Não consegui listar as saídas para a captura do microfone. Verifique se o player está disponível. Detalhes: " + ShortError(ex.Message));
                return;
            }
            if (devices.Count == 0)
            {
                AnnounceStatus("Não encontrei saídas de áudio. Conecte a Line 1, fone ou alto-falante e tente novamente.");
                return;
            }
            using (var form = new Form())
            {
                form.Text = "Saída da captura do microfone";
                form.Size = new Size(640, 420);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Saída da captura do microfone" };
                list.AccessibleDescription = "Escolha Line 1, um fone ou outro alto-falante para receber o microfone.";
                foreach (AudioDevice device in devices) list.Items.Add(device);
                int selected = devices.FindIndex(d => String.Equals(d.Id, selectedMicOutputDeviceId, StringComparison.OrdinalIgnoreCase));
                list.SelectedIndex = selected >= 0 ? selected : 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Usar esta saída", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK || !(list.SelectedItem is AudioDevice)) return;
                AudioDevice chosen = (AudioDevice)list.SelectedItem;
                selectedMicOutputDeviceId = chosen.Id;
                selectedMicOutputDeviceName = chosen.ToString();
                SaveConfig();
                if (micMonitorEnabled) RestartMicMonitor();
                AnnounceStatus("Saída do microfone definida para " + chosen + ".");
            }
        }

        private List<string> ListInputDevices()
        {
            string ffmpeg = GetPortableTool(Path.Combine("FFmpeg", "bin", "ffmpeg.exe"));
            if (String.IsNullOrWhiteSpace(ffmpeg)) ffmpeg = RunWhere("ffmpeg.exe");
            if (String.IsNullOrWhiteSpace(ffmpeg)) return new List<string>();
            string output = RunProcessCombined(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy", 15000);
            var result = new List<string>();
            bool audio = false;
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = line.Trim();
                if (text.Contains("DirectShow audio devices")) { audio = true; continue; }
                if (text.Contains("DirectShow video devices")) { audio = false; continue; }
                bool audioLine = audio || text.IndexOf("(audio)", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!audioLine) continue;
                Match match = Regex.Match(text, "\"([^\"]+)\"");
                if (match.Success && !text.Contains("Alternative name"))
                {
                    string name = match.Groups[1].Value;
                    if (!result.Any(x => String.Equals(x, name, StringComparison.OrdinalIgnoreCase))) result.Add(name);
                }
            }
            return result;
        }

        private string RunProcessCombined(string fileName, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(prefix))
                    psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
            }
            catch { }
            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (timeoutMs > 0 && !process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return output + "\r\n" + error;
                }
                return output + "\r\n" + error;
            }
        }

        private void ToggleMicMonitor()
        {
            micMonitorEnabled = !micMonitorEnabled;
            SaveConfig();
            if (micMonitorEnabled) StartMicMonitor();
            else StopMicMonitor();
            AnnounceStatus(micMonitorEnabled ? "Microfone ligado no player." : "Microfone desligado do player.");
        }

        private void ToggleMicMute()
        {
            micMuted = !micMuted;
            SaveConfig();
            ApplyMicMute();
            AnnounceStatus(micMuted ? "Microfone mutado." : "Microfone aberto.");
        }

        private void ConfigureMicVolume()
        {
            using (var form = new Form())
            {
                form.Text = "Volume do microfone";
                form.Size = new Size(420, 170);
                form.StartPosition = FormStartPosition.CenterParent;
                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);
                var label = new Label { Text = "Digite o volume do microfone, de 0 a 200.", AutoSize = true };
                panel.Controls.Add(label, 0, 0);
                var box = new TextBox { Text = micVolume.ToString(System.Globalization.CultureInfo.InvariantCulture), AccessibleName = "Volume do microfone" };
                panel.Controls.Add(box, 0, 1);
                var ok = new Button { Text = "Salvar", Dock = DockStyle.Top, DialogResult = DialogResult.OK };
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK) return;
                int value;
                if (!Int32.TryParse(box.Text.Trim(), out value)) { AnnounceStatus("Valor inválido."); return; }
                micVolume = Math.Max(0, Math.Min(200, value));
                SaveConfig();
                ApplyMicVolume();
                AnnounceStatus("Volume do microfone definido para " + micVolume + " por cento.");
            }
        }

        private void ChooseAudioListenMode()
        {
            using (var form = new Form())
            {
                form.Text = "Modo de escuta";
                form.Size = new Size(520, 300);
                form.StartPosition = FormStartPosition.CenterParent;
                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Modo de escuta";
                list.Items.Add("Microfone desligado");
                list.Items.Add("Vídeo e microfone");
                list.Items.Add("Somente microfone");
                list.SelectedIndex = audioListenMode == "microphone" ? 2 : audioListenMode == "both" ? 1 : 0;
                form.Controls.Add(list);
                var ok = new Button { Text = "Aplicar", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                if (form.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
                audioListenMode = list.SelectedIndex == 2 ? "microphone" : list.SelectedIndex == 1 ? "both" : "video";
                SaveConfig();
                ApplyListenModeToPlayers();
                AnnounceStatus("Modo de escuta: " + AudioListenModeLabel() + ".");
            }
        }

        private void StartMicMonitor()
        {
            if (String.IsNullOrWhiteSpace(selectedInputDeviceName))
            {
                AnnounceStatus("Escolha um microfone primeiro.");
                micMonitorEnabled = false;
                SaveConfig();
                return;
            }
            if (audioListenMode == "video") audioListenMode = "both";
            StopMicMonitor();
            string helper = Path.Combine(libraryDir, "mic_monitor.py");
            if (!File.Exists(helper))
            {
                AnnounceStatus("Arquivo do monitor de microfone não encontrado.");
                return;
            }
            string args = "\"" + EscapeArg(helper) + "\" \"" + EscapeArg(selectedInputDeviceName) + "\" \"" + EscapeArg(selectedMicOutputDeviceId) + "\" " +
                micVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + micMuted.ToString().ToLowerInvariant();
            var psi = new ProcessStartInfo(GetPythonFileName(), args);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                psi.EnvironmentVariables["YOUTUBE_LIGHT_CONFIG_DIR"] = configDir;
                psi.EnvironmentVariables["YOUTUBE_LIGHT_LIBRARY_DIR"] = libraryDir;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string vlcDir = FindVlcDirectory();
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(vlcDir)) prefix = vlcDir + ";" + prefix;
                if (!String.IsNullOrWhiteSpace(prefix)) psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
            }
            catch { }
            micMonitorProcess = Process.Start(psi);
            micMonitorInput = micMonitorProcess.StandardInput;
            string ready = ReadMicMonitorLine(5000);
            if (String.IsNullOrWhiteSpace(ready) || !ready.Contains("\"ok\": true"))
            {
                StopMicMonitor();
                AnnounceStatus("Não consegui ativar o microfone no player.");
                return;
            }
            ApplyListenModeToPlayers();
        }

        private void RestartMicMonitor()
        {
            if (!micMonitorEnabled) return;
            StartMicMonitor();
        }

        private void StopMicMonitor()
        {
            try { SendMicMonitorCommand("{\"command\":\"stop\"}", false); } catch { }
            try
            {
                if (micMonitorProcess != null && !micMonitorProcess.HasExited)
                    micMonitorProcess.Kill();
            }
            catch { }
            micMonitorProcess = null;
            micMonitorInput = null;
        }

        private string ReadMicMonitorLine(int timeoutMs)
        {
            if (micMonitorProcess == null) return "";
            var task = Task.Factory.StartNew(delegate { return micMonitorProcess.StandardOutput.ReadLine(); });
            return task.Wait(timeoutMs) ? (task.Result ?? "") : "";
        }

        private void SendMicMonitorCommand(string json, bool waitReply)
        {
            lock (micMonitorLock)
            {
                if (micMonitorProcess == null || micMonitorProcess.HasExited || micMonitorInput == null) return;
                micMonitorInput.WriteLine(json);
                micMonitorInput.Flush();
                if (waitReply) ReadMicMonitorLine(1000);
            }
        }

        private void ApplySelectedOutputDeviceToMicMonitor()
        {
            SendMicMonitorCommand("{\"command\":\"set-device\",\"id\":\"" + JsonEscape(selectedMicOutputDeviceId) + "\"}", true);
        }

        private void ApplyMicMute()
        {
            SendMicMonitorCommand("{\"command\":\"set-muted\",\"muted\":" + micMuted.ToString().ToLowerInvariant() + "}", true);
        }

        private void ApplyMicVolume()
        {
            SendMicMonitorCommand("{\"command\":\"set-volume\",\"volume\":" + micVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", true);
        }

        private void ApplyListenModeToPlayers()
        {
            if (audioListenMode == "video")
            {
                StopMicMonitor();
                return;
            }
            if (micMonitorEnabled && (micMonitorProcess == null || micMonitorProcess.HasExited))
                StartMicMonitor();
        }

        private void ApplyVideoVolume(int volume)
        {
            try
            {
                int value = Math.Max(0, Math.Min(volumeBoostPercent, volume));
                if (usingVlc) SendVlcCommand("{\"command\":\"set-volume\",\"volume\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", false);
                else if (usingMpv) SendMpvCommand("{\"command\":\"set-volume\",\"volume\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                else if (internalPlayer != null)
                {
                    object settings = GetComProperty(internalPlayer, "settings");
                    SetComProperty(settings, "volume", Math.Max(0, Math.Min(100, value)));
                }
            }
            catch { }
        }

        private void SetupTrayIcon()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Text = "Youtube Light";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Visible = false;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Restaurar", null, delegate { RestoreFromTray(); });
            menu.Items.Add("Pausar ou retomar", null, delegate { TogglePause(); });
            menu.Items.Add("Próxima música", null, delegate { PlayRelative(1); });
            menu.Items.Add("Sair", null, delegate { RequestExit(); });
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void MainFormResize(object sender, EventArgs e)
        {
            if (trayIcon != null) trayIcon.Visible = false;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            if (feedList != null) feedList.Focus();
        }
        private void RequestExit()
        {
            if (!ConfirmExit()) return;
            try { trayIcon.Visible = false; } catch { }
            Close();
        }

        private bool ConfirmExit()
        {
            if (isExitPromptOpen) return false;
            isExitPromptOpen = true;
            try
            {
                using (var form = new Form())
                {
                    form.Text = "Sair do Youtube Light";
                    form.StartPosition = FormStartPosition.CenterParent;
                    form.Size = new Size(360, 180);
                    form.MinimizeBox = false;
                    form.MaximizeBox = false;
                    form.ShowInTaskbar = false;
                    form.KeyPreview = true;

                    var panel = new TableLayoutPanel();
                    panel.Dock = DockStyle.Fill;
                    panel.RowCount = 2;
                    panel.ColumnCount = 1;
                    panel.Padding = new Padding(12);
                    form.Controls.Add(panel);

                    var label = new Label();
                    label.Dock = DockStyle.Fill;
                    label.Text = "Deseja fechar o programa? Use as setas para escolher e Enter para confirmar.";
                    label.AutoSize = true;
                    panel.Controls.Add(label, 0, 0);

                    var list = new ListBox();
                    list.Dock = DockStyle.Fill;
                    list.Items.Add("Nao");
                    list.Items.Add("Sim");
                    list.SelectedIndex = 0;
                    panel.Controls.Add(list, 0, 1);
                    form.AcceptButton = null;
                    form.CancelButton = null;
                    form.Shown += delegate { list.Focus(); };
                    form.KeyDown += delegate(object sender, KeyEventArgs e)
                    {
                        if (e.KeyCode == Keys.Escape)
                        {
                            form.DialogResult = DialogResult.No;
                            form.Close();
                            return;
                        }
                        if (e.KeyCode == Keys.Enter)
                        {
                            form.DialogResult = list.SelectedIndex == 1 ? DialogResult.Yes : DialogResult.No;
                            form.Close();
                            return;
                        }
                    };
                    if (form.ShowDialog(this) == DialogResult.Yes)
                    {
                        StopPlayback();
                        return true;
                    }
                }
            }
            finally
            {
                isExitPromptOpen = false;
            }
            return false;
        }

        private string ShortcutActionFromIndex(int index)
        {
            string[] actions = new[] { "search", "pause", "seekBack", "seekForward", "volumeUp", "volumeDown", "volume", "time", "link", "next", "previous", "defaults", "close" };
            if (index < 0 || index >= actions.Length) return "";
            return actions[index];
        }

        private Keys CaptureShortcut(IWin32Window owner)
        {
            using (var form = new Form())
            {
                form.Text = "Pressione o novo atalho";
                form.Size = new Size(460, 160);
                form.StartPosition = FormStartPosition.CenterParent;
                form.KeyPreview = true;
                var label = new Label();
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Text = "Pressione a combinação desejada. Escape cancela.";
                label.AccessibleName = "Pressione a combinação desejada. Escape cancela.";
                form.Controls.Add(label);
                Keys captured = Keys.None;
                form.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape) { form.Close(); return; }
                    e.SuppressKeyPress = true;
                    if (IsModifierOnlyShortcut(e.KeyData))
                    {
                        label.Text = "Agora pressione a tecla principal junto do modificador.";
                        label.AccessibleName = label.Text;
                        Speak(label.Text);
                        return;
                    }
                    captured = e.KeyData;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };
                form.ShowDialog(owner);
                return captured;
            }
        }

        private bool IsModifierOnlyShortcut(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            return keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey || keyCode == Keys.Menu ||
                keyCode == Keys.LControlKey || keyCode == Keys.RControlKey ||
                keyCode == Keys.LShiftKey || keyCode == Keys.RShiftKey ||
                keyCode == Keys.LMenu || keyCode == Keys.RMenu;
        }

        private string GetDownloadDir()
        {
            if (String.IsNullOrWhiteSpace(selectedDownloadDir)) return defaultDownloadDir;
            return selectedDownloadDir;
        }

        private void ChooseDownloadFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Escolha a pasta onde as músicas serão baixadas.";
                dialog.SelectedPath = Directory.Exists(GetDownloadDir()) ? GetDownloadDir() : defaultDownloadDir;
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedDownloadDir = dialog.SelectedPath;
                Directory.CreateDirectory(selectedDownloadDir);
                SaveConfig();
                AnnounceStatus("Pasta de downloads definida: " + selectedDownloadDir + ".");
            }
        }

        private void UseDefaultDownloadFolder()
        {
            string windowsDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            selectedDownloadDir = Directory.Exists(windowsDownloads) ? windowsDownloads : defaultDownloadDir;
            Directory.CreateDirectory(selectedDownloadDir);
            SaveConfig();
            AnnounceStatus("Usando a pasta Downloads do Windows: " + selectedDownloadDir + ".");
        }

        private void ExportResults()
        {
            if (tracks.Count == 0) { SetStatus("Não há resultados para exportar."); return; }
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Exportar resultados";
                dialog.Filter = "Texto simples (*.txt)|*.txt|Playlist M3U (*.m3u)|*.m3u|CSV (*.csv)|*.csv";
                dialog.FileName = "youtube_light_resultados.txt";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                var builder = new StringBuilder();
                if (ext == ".m3u")
                {
                    builder.AppendLine("#EXTM3U");
                    foreach (Track track in tracks)
                        builder.AppendLine(TrackUrl(track));
                }
                else if (ext == ".csv")
                {
                    builder.AppendLine("titulo,canal,duracao,link");
                    foreach (Track track in tracks)
                        builder.AppendLine(Csv(track.Title) + "," + Csv(track.Channel) + "," + Csv(track.Duration) + "," + Csv(TrackUrl(track)));
                }
                else
                {
                    foreach (Track track in tracks)
                        builder.AppendLine(track.Title + " - " + track.Channel + " - " + TrackUrl(track));
                }
                File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
                AnnounceStatus("Resultados exportados.");
            }
        }

        private string Csv(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }

        private void ExportBackup()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Exportar backup";
                dialog.Filter = "Backup JSON (*.json)|*.json";
                dialog.FileName = "youtube_light_backup.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SaveConfig();
                SaveLocalData();
                var backup = new Dictionary<string, object>();
                backup["player_config"] = File.Exists(configFile) ? File.ReadAllText(configFile, Encoding.UTF8) : "";
                backup["favoritos_locais"] = File.Exists(LocalDataFile("favoritos_locais")) ? File.ReadAllText(LocalDataFile("favoritos_locais"), Encoding.UTF8) : "[]";
                backup["historico_local"] = File.Exists(LocalDataFile("historico_local")) ? File.ReadAllText(LocalDataFile("historico_local"), Encoding.UTF8) : "[]";
                backup["fila_reproducao"] = File.Exists(LocalDataFile("fila_reproducao")) ? File.ReadAllText(LocalDataFile("fila_reproducao"), Encoding.UTF8) : "[]";
                File.WriteAllText(dialog.FileName, serializer.Serialize(backup), Encoding.UTF8);
                AnnounceStatus("Backup exportado.");
            }
        }

        private void ImportBackup()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Importar backup";
                dialog.Filter = "Backup JSON (*.json)|*.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var backup = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                WriteBackupText(backup, "player_config", configFile);
                WriteBackupText(backup, "favoritos_locais", LocalDataFile("favoritos_locais"));
                WriteBackupText(backup, "historico_local", LocalDataFile("historico_local"));
                WriteBackupText(backup, "fila_reproducao", LocalDataFile("fila_reproducao"));
                LoadConfig();
                ApplyDefaultShortcuts();
                LoadLocalData();
                AnnounceStatus("Backup importado.");
            }
        }

        private void WriteBackupText(Dictionary<string, object> backup, string key, string target)
        {
            object value;
            if (backup == null || !backup.TryGetValue(key, out value) || value == null) return;
            File.WriteAllText(target, Convert.ToString(value), Encoding.UTF8);
        }

        private void RunDiagnostics()
        {
            SetProgress(true);
            SetStatus("Executando diagnóstico.");
            RunWorker(delegate
            {
                var lines = new List<string>();
                lines.Add("Diagnóstico do Youtube Light");
                lines.Add("");
                lines.Add(PortableRuntimeLooksComplete() ? "Runtime portátil: OK" : "Runtime portátil: incompleto");
                lines.Add(CheckTool("Python", GetPythonFileName(), "--version"));
                lines.Add(CheckTool("yt-dlp", File.Exists(GetStandaloneYtdlpPath()) ? GetStandaloneYtdlpPath() : GetPythonFileName(), File.Exists(GetStandaloneYtdlpPath()) ? "--version" : "-m yt_dlp --version"));
                lines.Add(CheckTool("youtube-dl", GetYoutubeDlPath(), "--version"));
                lines.Add(CanStartPythonVlc() ? "VLC com python-vlc: OK" : "VLC com python-vlc: não disponível");
                lines.Add(String.IsNullOrEmpty(GetPortableTool(Path.Combine("Node", "node.exe"))) && String.IsNullOrEmpty(RunWhere("node.exe")) ? "Node.js: não encontrado" : "Node.js: OK");
                lines.Add(String.IsNullOrEmpty(GetPortableTool(Path.Combine("FFmpeg", "bin", "ffmpeg.exe"))) && String.IsNullOrEmpty(RunWhere("ffmpeg.exe")) ? "FFmpeg: não encontrado" : "FFmpeg: OK");
                lines.Add(IsLoggedIn() ? "Login do YouTube Music: arquivo de sessão encontrado" : "Login do YouTube Music: não encontrado");
                lines.Add("Pasta de downloads: " + GetDownloadDir());
                lines.Add("Fila: " + playbackQueue.Count + " itens");
                lines.Add("Favoritos locais: " + localFavorites.Count + " itens");
                lines.Add("Histórico local: " + localHistory.Count + " itens");
                string report = String.Join("\r\n", lines.ToArray());
                BeginInvoke(new Action(delegate
                {
                    SetProgress(false);
                    using (var form = new Form())
                    {
                        form.Text = "Diagnóstico";
                        form.Size = new Size(720, 520);
                        form.StartPosition = FormStartPosition.CenterParent;
                        var box = new TextBox();
                        box.Multiline = true;
                        box.ReadOnly = true;
                        box.ScrollBars = ScrollBars.Both;
                        box.Dock = DockStyle.Fill;
                        box.Text = report;
                        box.AccessibleName = "Resultado do diagnóstico";
                        form.Controls.Add(box);
                        form.ShowDialog(this);
                    }
                    AnnounceStatus("Diagnóstico concluído.");
                }));
            }, true);
        }

        private string CheckTool(string label, string file, string args)
        {
            try
            {
                string output = RunProcess(file, args, 30000, false).Trim();
                string first = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return label + ": OK" + (String.IsNullOrWhiteSpace(first) ? "" : " - " + first);
            }
            catch (Exception ex)
            {
                return label + ": erro - " + ShortError(ex.Message);
            }
        }

        private void ShowAbout()
        {
            string message =
                "Youtube Light versão " + AppVersion + ".\r\n" +
                "Atualizado em " + AppUpdatedAt + ".\r\n" +
                "Modo portátil completo. O pacote inclui runtime interno com Python, VLC, yt-dlp, youtube-dl, FFmpeg e Node quando distribuído pela versão completa.\r\n" +
                "Modo padrão: YouTube completo, com busca por músicas, playlists e canais. Canais carregam vídeos recentes dentro do aplicativo.\r\n" +
                "O atualizador substitui os arquivos dentro da própria pasta onde o aplicativo foi aberto.\r\n" +
                "Criado por Diego Vinicius Carmo Grando.";
            AnnounceStatus("Youtube Light versão " + AppVersion + ".");
            MessageBox.Show(message, Text);
        }

        private void ShowHelp()
        {
            MessageBox.Show(
                "Atalhos:\r\n" +
                "Control P: abre a busca principal. Primeiro digite a pesquisa; depois escolha músicas, playlists ou canais.\r\n" +
                "Alt 2: abre Logar com Google.\r\n" +
                "Enter nos resultados: toca ou abre playlist.\r\n" +
                "Aplicação ou Shift F10 nos resultados: menu com curtir, descurtir e adicionar à playlist.\r\n" +
                "No player: Alt P, P ou Espaço pausa. Setas esquerda e direita voltam ou avançam 10 segundos. Setas cima e baixo mudam volume.\r\n" +
                "No player: Alt Shift seta para direita avança o tempo configurado. Alt Shift seta para esquerda volta o tempo configurado. Alt Shift seta para cima aumenta volume. Alt Shift seta para baixo diminui volume.\r\n" +
                "No player: V anuncia volume. T anuncia tempo. L copia link. N próxima. B anterior. R alterna repetição e aleatório quando a pasta carregada tem apenas áudio.\r\n" +
                "Letras puras funcionam só dentro do aplicativo. Atalhos personalizados também são locais e só funcionam quando o Youtube Light está em foco.\r\n" +
                "Control Direita e Control Esquerda também vão para próxima ou anterior.\r\n" +
                "No player, Aplicação ou Shift F10 abre ações da música atual, incluindo curtir, baixar, copiar link e trocar dispositivo de saída.\r\n" +
                "Em Configurações, Áudio e transmissão permite escolher saída principal para transmissão, retorno do player no fone, microfone, mute do microfone, volume do microfone e modo de escuta. Para TeamTalk, use saída principal na Line e retorno do player no fone.\r\n" +
                "Pressione Alt para abrir o menu principal acessível. Ele abre com tudo recolhido. Use seta para cima e para baixo para navegar. Enter ou seta direita expande ou executa. Seta esquerda recolhe.\r\n" +
                "No menu Busca, você pode pesquisar vídeos do YouTube com filtro de vídeos, músicas, playlists ou canais, e também pesquisar últimos vídeos ou músicas por país.\r\n" +
                "No menu Player do PC, você pode abrir uma pasta de mídia. O app tenta tocar MP3, WAV, FLAC, M4A, OGG, OPUS, WMA, MP4, MKV, AVI, MOV, WEBM e outros formatos pelo VLC portátil.\r\n" +
                "No menu Conversor, você pode converter áudio ou vídeo para outros formatos, e também transformar áudio em vídeo MP4 com fundo preto usando FFmpeg.\r\n" +
                "No menu de aplicações de um resultado ou do player, você pode abrir descrição, comentários, capítulos, legendas e vídeos relacionados quando o YouTube disponibilizar esses dados.\r\n" +
                "Em Configurações, dá para ligar notificações de vídeos novos das inscrições, escolher leitura automática e intervalo de verificação.\r\n" +
                "F5 atualiza dependências.\r\n" +
                "Em Mais opções, mude para YouTube Music, faça login, veja conta, atualize dependências, escolha a pasta de downloads, abra a pasta de downloads, envie ideias, veja sobre ou saia.",
                Text);
        }

        private string GetYtdlpCookieArgs()
        {
            string cookieFile = Path.Combine(configDir, "cookies.txt");
            if (File.Exists(cookieFile)) return "--cookies \"" + EscapeArg(cookieFile) + "\" ";
            // Não tente descriptografar bancos do Chrome/Edge automaticamente.
            // O DPAPI pode falhar quando o navegador está aberto, foi instalado
            // por outro usuário ou quando a chave pertence a outro contexto.
            // Vídeos públicos funcionam sem cookies e o login exportado continua
            // sendo usado pelo arquivo local acima.
            return "";
        }

        private string SelectBrowser(string prompt, out string browserName, out string browserCode)
        {
            browserName = "";
            browserCode = "";
            string edge = FindEdge();
            string chrome = FindChrome();

            using (var form = new Form())
            {
                form.Text = "Escolher navegador";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Size = new Size(420, 220);
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                var panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.Padding = new Padding(12);
                panel.RowCount = 3;
                panel.ColumnCount = 1;
                form.Controls.Add(panel);

                var label = new Label();
                label.Text = prompt;
                label.AutoSize = true;
                panel.Controls.Add(label, 0, 0);

                var list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.AccessibleName = "Navegador para login";
                if (!String.IsNullOrEmpty(edge)) list.Items.Add("Microsoft Edge");
                if (!String.IsNullOrEmpty(chrome)) list.Items.Add("Google Chrome");
                if (list.Items.Count == 0) return "";
                list.SelectedIndex = 0;
                panel.Controls.Add(list, 0, 1);

                var ok = new Button();
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.AutoSize = true;
                panel.Controls.Add(ok, 0, 2);
                form.AcceptButton = ok;

                if (form.ShowDialog(this) != DialogResult.OK || list.SelectedItem == null) return "";
                browserName = list.SelectedItem.ToString();
                if (browserName == "Microsoft Edge")
                {
                    browserCode = "edge";
                    return edge;
                }
                browserCode = "chrome";
                return chrome;
            }
        }

        private string FindEdge()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string edge = Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(edge)) return edge;
            pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            edge = Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(edge)) return edge;
            return RunWhere("msedge.exe");
        }

        private string FindChrome()
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(local)) return local;
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string chrome = Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(chrome)) return chrome;
            pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            chrome = Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(chrome)) return chrome;
            return RunWhere("chrome.exe");
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(configFile)) return;
                foreach (string line in File.ReadAllLines(configFile, Encoding.UTF8))
                {
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;
                    if (parts[0].Trim().Equals("volume", StringComparison.OrdinalIgnoreCase))
                    {
                        int volume;
                        if (Int32.TryParse(parts[1].Trim(), out volume))
                        {
                            savedVolume = Math.Max(0, Math.Min(200, volume));
                            hasSavedVolume = true;
                        }
                    }
                    else if (parts[0].Trim().Equals("downloadDir", StringComparison.OrdinalIgnoreCase))
                    {
                        string dir = parts[1].Trim();
                        if (!String.IsNullOrWhiteSpace(dir)) selectedDownloadDir = dir;
                    }
                    else if (parts[0].Trim().Equals("selectedOutputDeviceId", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedOutputDeviceId = parts[1].Trim();
                    }
                    else if (parts[0].Trim().Equals("selectedOutputDeviceName", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedOutputDeviceName = CleanDeviceName(parts[1].Trim());
                    }
                    else if (parts[0].Trim().Equals("selectedInputDeviceName", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedInputDeviceName = parts[1].Trim();
                    }
                    else if (parts[0].Trim().Equals("selectedMicOutputDeviceId", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedMicOutputDeviceId = parts[1].Trim();
                    }
                    else if (parts[0].Trim().Equals("selectedMicOutputDeviceName", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedMicOutputDeviceName = CleanDeviceName(parts[1].Trim());
                    }
                    else if (parts[0].Trim().Equals("selectedMonitorOutputDeviceId", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedMonitorOutputDeviceId = parts[1].Trim();
                    }
                    else if (parts[0].Trim().Equals("selectedMonitorOutputDeviceName", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedMonitorOutputDeviceName = CleanDeviceName(parts[1].Trim());
                    }
                    else if (parts[0].Trim().Equals("playerMonitorEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        playerMonitorEnabled = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("playerMonitorVolume", StringComparison.OrdinalIgnoreCase))
                    {
                        int value;
                        if (Int32.TryParse(parts[1].Trim(), out value))
                            playerMonitorVolume = Math.Max(0, Math.Min(200, value));
                    }
                    else if (parts[0].Trim().Equals("micMonitorEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        micMonitorEnabled = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("micMuted", StringComparison.OrdinalIgnoreCase))
                    {
                        micMuted = !parts[1].Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("micVolume", StringComparison.OrdinalIgnoreCase))
                    {
                        int value;
                        if (Int32.TryParse(parts[1].Trim(), out value))
                            micVolume = Math.Max(0, Math.Min(200, value));
                    }
                    else if (parts[0].Trim().Equals("audioListenMode", StringComparison.OrdinalIgnoreCase))
                    {
                        string mode = parts[1].Trim();
                        if (mode == "video" || mode == "both" || mode == "microphone") audioListenMode = mode;
                    }
                    else if (parts[0].Trim().Equals("announcePlayerEvents", StringComparison.OrdinalIgnoreCase))
                    {
                        announcePlayerEvents = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("infiniteRadio", StringComparison.OrdinalIgnoreCase))
                    {
                        infiniteRadio = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("autoplayEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        autoplayEnabled = !parts[1].Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("minimizeToTray", StringComparison.OrdinalIgnoreCase))
                    {
                        // Opção antiga mantida apenas para compatibilidade com configurações já existentes.
                    }
                    else if (parts[0].Trim().Equals("normalizeVolume", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizeVolume = !parts[1].Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("musicOnlyMode", StringComparison.OrdinalIgnoreCase))
                    {
                        musicOnlyMode = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("globalPlayerShortcuts", StringComparison.OrdinalIgnoreCase))
                    {
                        // Opção antiga mantida apenas para compatibilidade com configurações já existentes.
                    }
                    else if (parts[0].Trim().Equals("preferTemporaryAudio", StringComparison.OrdinalIgnoreCase))
                    {
                        preferTemporaryAudio = !parts[1].Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("realtimeVideoNotifications", StringComparison.OrdinalIgnoreCase))
                    {
                        realtimeVideoNotifications = parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("autoReadVideoNotifications", StringComparison.OrdinalIgnoreCase))
                    {
                        autoReadVideoNotifications = !parts[1].Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (parts[0].Trim().Equals("notificationIntervalMinutes", StringComparison.OrdinalIgnoreCase))
                    {
                        int minutes;
                        if (Int32.TryParse(parts[1].Trim(), out minutes))
                            notificationIntervalMinutes = Math.Max(5, Math.Min(1440, minutes));
                    }
                    else if (parts[0].Trim().Equals("volumeBoostPercent", StringComparison.OrdinalIgnoreCase))
                    {
                        int boost;
                        if (Int32.TryParse(parts[1].Trim(), out boost))
                            volumeBoostPercent = Math.Max(100, Math.Min(200, boost));
                    }
                    else if (parts[0].Trim().Equals("altShiftSeekSeconds", StringComparison.OrdinalIgnoreCase))
                    {
                        int seconds;
                        if (Int32.TryParse(parts[1].Trim(), out seconds))
                            altShiftSeekSeconds = Math.Max(1, Math.Min(300, seconds));
                    }
                    else if (parts[0].Trim().StartsWith("shortcut.", StringComparison.OrdinalIgnoreCase))
                    {
                        Keys parsed;
                        if (Enum.TryParse(parts[1].Trim(), out parsed))
                            customShortcuts[parts[0].Trim().Substring(9)] = parsed;
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine("volume=" + savedVolume);
                builder.AppendLine("downloadDir=" + GetDownloadDir());
                builder.AppendLine("selectedOutputDeviceId=" + selectedOutputDeviceId);
                builder.AppendLine("selectedOutputDeviceName=" + CleanDeviceName(selectedOutputDeviceName));
                builder.AppendLine("selectedInputDeviceName=" + selectedInputDeviceName);
                builder.AppendLine("selectedMicOutputDeviceId=" + selectedMicOutputDeviceId);
                builder.AppendLine("selectedMicOutputDeviceName=" + CleanDeviceName(selectedMicOutputDeviceName));
                builder.AppendLine("selectedMonitorOutputDeviceId=" + selectedMonitorOutputDeviceId);
                builder.AppendLine("selectedMonitorOutputDeviceName=" + CleanDeviceName(selectedMonitorOutputDeviceName));
                builder.AppendLine("playerMonitorEnabled=" + playerMonitorEnabled.ToString().ToLowerInvariant());
                builder.AppendLine("playerMonitorVolume=" + playerMonitorVolume);
                builder.AppendLine("micMonitorEnabled=" + micMonitorEnabled.ToString().ToLowerInvariant());
                builder.AppendLine("micMuted=" + micMuted.ToString().ToLowerInvariant());
                builder.AppendLine("micVolume=" + micVolume);
                builder.AppendLine("audioListenMode=" + audioListenMode);
                builder.AppendLine("announcePlayerEvents=" + announcePlayerEvents.ToString().ToLowerInvariant());
                builder.AppendLine("infiniteRadio=" + infiniteRadio.ToString().ToLowerInvariant());
                builder.AppendLine("autoplayEnabled=" + autoplayEnabled.ToString().ToLowerInvariant());
                builder.AppendLine("normalizeVolume=" + normalizeVolume.ToString().ToLowerInvariant());
                builder.AppendLine("musicOnlyMode=" + musicOnlyMode.ToString().ToLowerInvariant());
                builder.AppendLine("preferTemporaryAudio=" + preferTemporaryAudio.ToString().ToLowerInvariant());
                builder.AppendLine("realtimeVideoNotifications=" + realtimeVideoNotifications.ToString().ToLowerInvariant());
                builder.AppendLine("autoReadVideoNotifications=" + autoReadVideoNotifications.ToString().ToLowerInvariant());
                builder.AppendLine("notificationIntervalMinutes=" + notificationIntervalMinutes);
                builder.AppendLine("volumeBoostPercent=" + volumeBoostPercent);
                builder.AppendLine("altShiftSeekSeconds=" + altShiftSeekSeconds);
                foreach (var shortcut in customShortcuts)
                    builder.AppendLine("shortcut." + shortcut.Key + "=" + shortcut.Value);
                File.WriteAllText(configFile, builder.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private string LocalDataFile(string name)
        {
            return Path.Combine(configDir, name + ".dat");
        }

        private int ClampAppVolume(int value)
        {
            return Math.Max(0, Math.Min(volumeBoostPercent, value));
        }

        private void LoadLocalData()
        {
            LoadTrackList(LocalDataFile("favoritos_locais"), localFavorites);
            LoadTrackList(LocalDataFile("historico_local"), localHistory);
            LoadTrackList(LocalDataFile("fila_reproducao"), playbackQueue);
        }

        private void SaveLocalData()
        {
            SaveTrackList(LocalDataFile("favoritos_locais"), localFavorites);
            SaveTrackList(LocalDataFile("historico_local"), localHistory);
            SaveTrackList(LocalDataFile("fila_reproducao"), playbackQueue);
        }

        private void LoadNotifiedVideos()
        {
            try
            {
                notifiedVideoKeys.Clear();
                if (!File.Exists(notifiedVideosFile)) return;
                var items = serializer.Deserialize<List<string>>(File.ReadAllText(notifiedVideosFile, Encoding.UTF8));
                if (items == null) return;
                foreach (string item in items)
                    if (!String.IsNullOrWhiteSpace(item)) notifiedVideoKeys.Add(item);
            }
            catch { }
        }

        private void SaveNotifiedVideos()
        {
            try
            {
                File.WriteAllText(notifiedVideosFile, serializer.Serialize(notifiedVideoKeys.Take(1000).ToList()), Encoding.UTF8);
            }
            catch { }
        }

        private void LoadTrackList(string file, List<Track> target)
        {
            try
            {
                target.Clear();
                if (!File.Exists(file)) return;
                var items = serializer.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(file, Encoding.UTF8));
                if (items == null) return;
                foreach (var item in items)
                {
                    target.Add(new Track
                    {
                        Kind = GetString(item, "Kind", GetString(item, "kind", "track")),
                        Title = GetString(item, "Title", GetString(item, "title", "Sem título")),
                        Channel = GetString(item, "Channel", GetString(item, "channel", "")),
                        Duration = GetString(item, "Duration", GetString(item, "duration", "")),
                        Url = GetString(item, "Url", GetString(item, "url", "")),
                        VideoId = GetString(item, "VideoId", GetString(item, "videoId", "")),
                        BrowseId = GetString(item, "BrowseId", GetString(item, "browseId", "")),
                        PlaylistId = GetString(item, "PlaylistId", GetString(item, "playlistId", "")),
                        LikeStatus = GetString(item, "LikeStatus", GetString(item, "likeStatus", "")),
                        Published = GetString(item, "Published", GetString(item, "published", ""))
                    });
                }
            }
            catch { }
        }

        private void SaveTrackList(string file, List<Track> source)
        {
            try
            {
                var items = new List<Dictionary<string, string>>();
                foreach (Track track in source)
                {
                    items.Add(new Dictionary<string, string>
                    {
                        { "kind", track.Kind },
                        { "title", track.Title },
                        { "channel", track.Channel },
                        { "duration", track.Duration },
                        { "url", TrackUrl(track) },
                        { "videoId", track.VideoId },
                        { "browseId", track.BrowseId },
                        { "playlistId", track.PlaylistId },
                        { "likeStatus", track.LikeStatus },
                        { "published", track.Published }
                    });
                }
                File.WriteAllText(file, serializer.Serialize(items), Encoding.UTF8);
            }
            catch { }
        }

        private void ApplyDefaultShortcuts()
        {
            SetDefaultShortcut("pause", Keys.Alt | Keys.P);
            SetDefaultShortcut("seekBack", Keys.Alt | Keys.Shift | Keys.Left);
            SetDefaultShortcut("seekForward", Keys.Alt | Keys.Shift | Keys.Right);
            SetDefaultShortcut("volumeUp", Keys.Alt | Keys.Shift | Keys.Up);
            SetDefaultShortcut("volumeDown", Keys.Alt | Keys.Shift | Keys.Down);
            SetDefaultShortcut("volume", Keys.V);
            SetDefaultShortcut("time", Keys.T);
            customShortcuts.Remove("title");
            SetDefaultShortcut("link", Keys.L);
            SetDefaultShortcut("next", Keys.N);
            SetDefaultShortcut("previous", Keys.B);
            SetDefaultShortcut("search", Keys.Control | Keys.P);
        }

        private void SetDefaultShortcut(string action, Keys value)
        {
            if (!customShortcuts.ContainsKey(action)) customShortcuts[action] = value;
        }

        private bool EnsureInternalPlayer()
        {
            if (internalPlayer != null) return true;
            try
            {
                Type playerType = Type.GetTypeFromProgID("WMPlayer.OCX");
                if (playerType == null)
                {
                    SetStatus("Windows Media Player não está disponível neste Windows.");
                    return false;
                }
                internalPlayer = Activator.CreateInstance(playerType);
                SetComProperty(internalPlayer, "uiMode", "none");
                object settings = GetComProperty(internalPlayer, "settings");
                SetComProperty(settings, "autoStart", true);
                ApplySavedVolume();
                SetStatus("Player interno pronto.");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Não consegui iniciar o player interno: " + ex.Message);
                return false;
            }
        }

        private int GetPlayerVolume()
        {
            object settings = GetComProperty(internalPlayer, "settings");
            object value = GetComProperty(settings, "volume");
            return Math.Max(0, Math.Min(100, Convert.ToInt32(value)));
        }

        private void SaveCurrentVolume()
        {
            try
            {
                if (internalPlayer == null) return;
                savedVolume = GetPlayerVolume();
                hasSavedVolume = true;
                SaveConfig();
            }
            catch { }
        }

        private void ApplySavedVolume()
        {
            if (internalPlayer == null || !hasSavedVolume) return;
            try
            {
                object settings = GetComProperty(internalPlayer, "settings");
                SetComProperty(settings, "volume", savedVolume);
            }
            catch { }
        }

        private object GetComProperty(object target, string name)
        {
            if (target == null) return null;
            return target.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, target, null);
        }

        private void SetComProperty(object target, string name, object value)
        {
            if (target == null) return;
            target.GetType().InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, target, new object[] { value });
        }

        private object CallComMethod(object target, string name, params object[] args)
        {
            if (target == null) return null;
            return target.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, target, args);
        }

        private string GetYtdlpFileName()
        {
            string standalone = GetStandaloneYtdlpPath();
            if (File.Exists(standalone)) return standalone;
            string portable = Path.Combine(runtimeDir, "Python", "Scripts", "yt-dlp.exe");
            if (File.Exists(portable)) return portable;
            string found = RunWhere("yt-dlp.exe");
            return String.IsNullOrEmpty(found) ? GetPythonFileName() : found;
        }

        private string GetYtdlpPrefixArgs()
        {
            if (File.Exists(GetStandaloneYtdlpPath())) return "";
            if (File.Exists(Path.Combine(runtimeDir, "Python", "Scripts", "yt-dlp.exe"))) return "";
            return String.IsNullOrEmpty(RunWhere("yt-dlp.exe")) ? "-m yt_dlp " : "";
        }

        private string GetStandaloneYtdlpPath()
        {
            return Path.Combine(runtimeDir, "yt-dlp.exe");
        }

        private string GetYoutubeDlPath()
        {
            return Path.Combine(runtimeDir, "youtube-dl.exe");
        }

        private string GetPythonFileName()
        {
            string portable = Path.Combine(runtimeDir, "Python", "python.exe");
            if (File.Exists(portable)) return portable;
            return "python";
        }

        private string GetPortableTool(string relativePath)
        {
            string path = Path.Combine(runtimeDir, relativePath);
            return File.Exists(path) ? path : "";
        }

        private string GetRuntimePathPrefix()
        {
            var parts = new List<string>();
            AddRuntimePath(parts, Path.Combine(runtimeDir, "Python"));
            AddRuntimePath(parts, Path.Combine(runtimeDir, "Python", "Scripts"));
            AddRuntimePath(parts, Path.Combine(runtimeDir, "MPV"));
            AddRuntimePath(parts, Path.Combine(runtimeDir, "VLC"));
            AddRuntimePath(parts, Path.Combine(runtimeDir, "FFmpeg", "bin"));
            AddRuntimePath(parts, Path.Combine(runtimeDir, "Node"));
            AddRuntimePath(parts, libraryDir);
            return String.Join(";", parts.ToArray());
        }

        private bool PortableRuntimeLooksComplete()
        {
            return File.Exists(Path.Combine(runtimeDir, "Python", "python.exe")) &&
                (File.Exists(GetStandaloneYtdlpPath()) || File.Exists(Path.Combine(runtimeDir, "Python", "Scripts", "yt-dlp.exe"))) &&
                File.Exists(GetYoutubeDlPath()) &&
                (File.Exists(Path.Combine(runtimeDir, "MPV", "libmpv-2.dll")) || File.Exists(Path.Combine(runtimeDir, "MPV", "mpv-2.dll")) || File.Exists(Path.Combine(runtimeDir, "MPV", "mpv-1.dll"))) &&
                File.Exists(Path.Combine(runtimeDir, "VLC", "libvlc.dll")) &&
                File.Exists(Path.Combine(runtimeDir, "FFmpeg", "bin", "ffmpeg.exe"));
        }

        private void AddRuntimePath(List<string> parts, string path)
        {
            if (!String.IsNullOrWhiteSpace(path) && Directory.Exists(path)) parts.Add(path);
        }

        private string GetYtdlpYoutubeArgs()
        {
            return GetYtdlpYoutubeArgsForClients("android,web,mweb,ios");
        }

        private string GetYtdlpYoutubeArgsForClients(string clients)
        {
            return "--extractor-args \"youtube:player_client=" + clients + "\" --no-warnings ";
        }

        private string RunWhere(string name)
        {
            try { return RunProcess("where.exe", name, 10000, false).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""; }
            catch { return ""; }
        }

        private void RunWorker(Action action, bool quiet = false)
        {
            var worker = new BackgroundWorker();
            worker.DoWork += delegate
            {
                try { action(); }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (!quiet) MessageBox.Show(ex.Message, Text);
                        SetStatus("Erro: " + ex.Message);
                    }));
                }
            };
            worker.RunWorkerAsync();
        }

        private string RunYtdlp(string arguments, int timeoutMs, bool throwOnError = true)
        {
            var errors = new List<string>();
            string standalone = GetStandaloneYtdlpPath();
            if (File.Exists(standalone))
            {
                try { return RunYtdlpExecutable(standalone, arguments, timeoutMs, throwOnError, errors, "yt-dlp standalone"); }
                catch
                {
                    if (!ytdlpRepairAttempted)
                    {
                        ytdlpRepairAttempted = true;
                        try
                        {
                            RepairStandaloneYtdlp();
                            return RunYtdlpExecutable(GetStandaloneYtdlpPath(), arguments, timeoutMs, throwOnError, errors, "yt-dlp standalone reparado");
                        }
                        catch (Exception repairEx)
                        {
                            errors.Add("reparo do yt-dlp standalone: " + ShortError(repairEx.Message));
                        }
                    }
                }
            }

            string portable = Path.Combine(runtimeDir, "Python", "Scripts", "yt-dlp.exe");
            if (!File.Exists(standalone) && File.Exists(portable))
            {
                try { return RunYtdlpExecutable(portable, arguments, timeoutMs, throwOnError, errors, "yt-dlp portátil"); }
                catch { }
            }

            string found = RunWhere("yt-dlp.exe");
            if (!String.IsNullOrWhiteSpace(found) && !String.Equals(found, portable, StringComparison.OrdinalIgnoreCase))
            {
                try { return RunYtdlpExecutable(found, arguments, timeoutMs, throwOnError, errors, "yt-dlp do Windows"); }
                catch { }
            }

            string youtubeDl = GetYoutubeDlPath();
            if (File.Exists(youtubeDl))
            {
                try { return RunProcess(youtubeDl, BuildYoutubeDlCompatibleArgs(arguments), timeoutMs, throwOnError); }
                catch (Exception ex) { errors.Add("youtube-dl portátil: " + ShortError(ex.Message)); }
            }

            if (!ytdlpRepairAttempted)
            {
                ytdlpRepairAttempted = true;
                try
                {
                    RepairStandaloneYtdlp();
                    return RunProcess(GetStandaloneYtdlpPath(), arguments, timeoutMs, throwOnError);
                }
                catch (Exception ex)
                {
                    errors.Add("reparo do yt-dlp standalone: " + ShortError(ex.Message));
                }
            }

            throw new Exception("Não consegui executar o yt-dlp. Use Mais opções, Atualizar dependências. Detalhes: " + String.Join(" / ", errors.Take(3).ToArray()));
        }

        private string RunYtdlpExecutable(string fileName, string arguments, int timeoutMs, bool throwOnError, List<string> errors, string label)
        {
            Exception last = null;
            foreach (string attempt in BuildYtdlpFallbackArguments(arguments))
            {
                try { return RunProcess(fileName, attempt, timeoutMs, throwOnError); }
                catch (Exception ex)
                {
                    last = ex;
                    errors.Add(label + ": " + ShortError(ex.Message));
                    if (!IsRecoverableYtdlpError(ex.Message)) break;
                }
            }
            throw last ?? new Exception(label + " falhou.");
        }

        private string RunYtdlpWithProgress(string arguments, int timeoutMs, string label)
        {
            var errors = new List<string>();
            string standalone = GetStandaloneYtdlpPath();
            if (File.Exists(standalone))
            {
                try { return RunYtdlpExecutableWithProgress(standalone, arguments, timeoutMs, label, errors, "yt-dlp standalone"); }
                catch
                {
                    if (!ytdlpRepairAttempted)
                    {
                        ytdlpRepairAttempted = true;
                        try
                        {
                            RepairStandaloneYtdlp();
                            return RunYtdlpExecutableWithProgress(GetStandaloneYtdlpPath(), arguments, timeoutMs, label, errors, "yt-dlp standalone reparado");
                        }
                        catch (Exception repairEx)
                        {
                            errors.Add("reparo do yt-dlp standalone: " + ShortError(repairEx.Message));
                        }
                    }
                }
            }

            string portable = Path.Combine(runtimeDir, "Python", "Scripts", "yt-dlp.exe");
            if (!File.Exists(standalone) && File.Exists(portable))
            {
                try { return RunYtdlpExecutableWithProgress(portable, arguments, timeoutMs, label, errors, "yt-dlp portátil"); }
                catch { }
            }

            string found = RunWhere("yt-dlp.exe");
            if (!String.IsNullOrWhiteSpace(found) && !String.Equals(found, portable, StringComparison.OrdinalIgnoreCase))
            {
                try { return RunYtdlpExecutableWithProgress(found, arguments, timeoutMs, label, errors, "yt-dlp do Windows"); }
                catch { }
            }

            string youtubeDl = GetYoutubeDlPath();
            if (File.Exists(youtubeDl))
            {
                try { return RunProcessWithProgress(youtubeDl, BuildYoutubeDlCompatibleArgs(arguments), timeoutMs, label); }
                catch (Exception ex) { errors.Add("youtube-dl portátil: " + ShortError(ex.Message)); }
            }

            if (!ytdlpRepairAttempted)
            {
                ytdlpRepairAttempted = true;
                try
                {
                    RepairStandaloneYtdlp();
                    return RunProcessWithProgress(GetStandaloneYtdlpPath(), arguments, timeoutMs, label);
                }
                catch (Exception ex)
                {
                    errors.Add("reparo do yt-dlp standalone: " + ShortError(ex.Message));
                }
            }

            throw new Exception("Não consegui executar o yt-dlp. Use Mais opções, Atualizar dependências. Detalhes: " + String.Join(" / ", errors.Take(3).ToArray()));
        }

        private string RunYtdlpExecutableWithProgress(string fileName, string arguments, int timeoutMs, string progressLabel, List<string> errors, string label)
        {
            Exception last = null;
            foreach (string attempt in BuildYtdlpFallbackArguments(arguments))
            {
                try { return RunProcessWithProgress(fileName, attempt, timeoutMs, progressLabel); }
                catch (Exception ex)
                {
                    last = ex;
                    errors.Add(label + ": " + ShortError(ex.Message));
                    if (!IsRecoverableYtdlpError(ex.Message)) break;
                }
            }
            throw last ?? new Exception(label + " falhou.");
        }

        private string BuildYoutubeDlCompatibleArgs(string arguments)
        {
            string clean = RemoveBrowserCookieArgs(arguments);
            clean = Regex.Replace(clean, "--extractor-args\\s+\"[^\"]*\"\\s*", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, "--compat-options\\s+\\S+\\s*", "", RegexOptions.IgnoreCase);
            return clean;
        }

        private List<string> BuildYtdlpFallbackArguments(string arguments)
        {
            var result = new List<string>();
            AddUniqueArgument(result, arguments);
            string noCookies = RemoveBrowserCookieArgs(arguments);
            AddUniqueArgument(result, noCookies);
            string broad = BroadenYtdlpFormat(arguments);
            AddUniqueArgument(result, broad);
            AddUniqueArgument(result, RemoveBrowserCookieArgs(broad));
            return result;
        }

        private void AddUniqueArgument(List<string> items, string value)
        {
            value = value ?? "";
            if (!items.Any(existing => String.Equals(existing, value, StringComparison.Ordinal))) items.Add(value);
        }

        private string BroadenYtdlpFormat(string arguments)
        {
            string clean = arguments ?? "";
            if (clean.IndexOf(" -f ", StringComparison.OrdinalIgnoreCase) < 0 && !clean.TrimStart().StartsWith("-f ", StringComparison.OrdinalIgnoreCase)) return clean;
            string format = clean.IndexOf("bestvideo", StringComparison.OrdinalIgnoreCase) >= 0 || clean.IndexOf("merge-output-format", StringComparison.OrdinalIgnoreCase) >= 0
                ? "bv*+ba/best"
                : "ba/bestaudio/best/b";
            clean = Regex.Replace(clean, "(^|\\s)-f\\s+\"[^\"]*\"", "$1-f \"" + format + "\"", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, "(^|\\s)-f\\s+\\S+", "$1-f \"" + format + "\"", RegexOptions.IgnoreCase);
            return clean;
        }

        private bool IsRecoverableYtdlpError(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return false;
            return IsBrowserCookieCopyError(message) || message.IndexOf("Requested format is not available", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBrowserCookieCopyError(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return false;
            return message.IndexOf("Could not copy Chrome cookie database", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Could not copy Edge cookie database", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Failed to decrypt with DPAPI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("cookie database", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string RemoveBrowserCookieArgs(string arguments)
        {
            string clean = arguments ?? "";
            clean = Regex.Replace(clean, "--cookies-from-browser\\s+\"[^\"]*\"\\s*", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, "--cookies-from-browser\\s+\\S+\\s*", "", RegexOptions.IgnoreCase);
            return clean;
        }

        private string RunProcess(string fileName, string arguments, int timeoutMs, bool throwOnError = true)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                psi.EnvironmentVariables["YOUTUBE_LIGHT_CONFIG_DIR"] = configDir;
                psi.EnvironmentVariables["YOUTUBE_LIGHT_LIBRARY_DIR"] = libraryDir;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(prefix))
                    psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
            }
            catch { }
            using (var process = new Process())
            {
                var output = new StringBuilder();
                var error = new StringBuilder();
                process.StartInfo = psi;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) error.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (timeoutMs > 0 && !process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    throw new Exception("Tempo esgotado ao executar " + fileName + ".");
                }
                process.WaitForExit();
                process.WaitForExit(1000);
                if (throwOnError && process.ExitCode != 0)
                {
                    string err = error.ToString().Trim();
                    string stdout = output.ToString().Trim();
                    if (!String.IsNullOrWhiteSpace(stdout) && stdout.StartsWith("{"))
                    {
                        try
                        {
                            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Last());
                            string parsed = GetString(data, "error", "");
                            if (!String.IsNullOrWhiteSpace(parsed)) throw new Exception(parsed);
                        }
                        catch (Exception parsedEx)
                        {
                            if (!String.IsNullOrWhiteSpace(parsedEx.Message)) throw;
                        }
                    }
                    throw new Exception(!String.IsNullOrWhiteSpace(err) ? err : "Comando falhou: " + fileName);
                }
                return output.ToString();
            }
        }

        private string RunProcessWithProgress(string fileName, string arguments, int timeoutMs, string label)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                psi.EnvironmentVariables["YOUTUBE_LIGHT_CONFIG_DIR"] = configDir;
                psi.EnvironmentVariables["YOUTUBE_LIGHT_LIBRARY_DIR"] = libraryDir;
                string oldPath = psi.EnvironmentVariables["PATH"] ?? "";
                string prefix = GetRuntimePathPrefix();
                if (!String.IsNullOrWhiteSpace(prefix))
                    psi.EnvironmentVariables["PATH"] = prefix + ";" + oldPath;
            }
            catch { }
            using (var process = new Process())
            {
                var output = new StringBuilder();
                var error = new StringBuilder();
                DataReceivedEventHandler handler = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    output.AppendLine(e.Data);
                    Match match = Regex.Match(e.Data, @"\[download\]\s+([0-9]+(?:\.[0-9]+)?)%");
                    if (match.Success)
                    {
                        double value;
                        if (Double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value))
                            SetProgressPercent((int)Math.Round(value), label);
                    }
                };
                process.StartInfo = psi;
                process.OutputDataReceived += handler;
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        error.AppendLine(e.Data);
                        handler(sender, e);
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (timeoutMs > 0 && !process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    throw new Exception("Tempo esgotado ao executar " + fileName + ".");
                }
                process.WaitForExit();
                process.WaitForExit(1000);
                if (process.ExitCode != 0)
                    throw new Exception(!String.IsNullOrWhiteSpace(error.ToString()) ? error.ToString().Trim() : "Comando falhou: " + fileName);
                SetProgressPercent(100, label);
                return output.ToString();
            }
        }

        private Process StartProcessNoWindow(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            return Process.Start(psi);
        }

        private void SetStatus(string message)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), message); return; }
            statusLabel.Text = message;
            statusLabel.AccessibleName = message;
            statusLabel.AccessibleDescription = message;
            AnnounceForScreenReader(message);
        }

        private void SetProgress(bool active)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool>(SetProgress), active); return; }
            if (progressBar == null) return;
            progressBar.Visible = active;
            progressBar.Style = active ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            progressBar.MarqueeAnimationSpeed = active ? 30 : 0;
            if (!active)
            {
                progressBar.Value = 0;
                lastProgressAnnouncement = -1;
            }
            progressBar.AccessibleName = active ? "Progresso em andamento" : "Progresso parado";
            progressBar.AccessibleDescription = active ? "Atualização ou download em andamento." : "Nenhuma atualização ou download em andamento.";
        }

        private void SetProgressPercent(int percent, string label)
        {
            if (InvokeRequired) { BeginInvoke(new Action<int, string>(SetProgressPercent), percent, label); return; }
            if (progressBar == null) return;
            percent = Math.Max(0, Math.Min(100, percent));
            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.MarqueeAnimationSpeed = 0;
            progressBar.Value = percent;
            string message = label + " " + percent + " por cento.";
            progressBar.AccessibleName = message;
            progressBar.AccessibleDescription = message;
            int announcedPercent = percent >= 100 ? 100 : (percent / 10) * 10;
            if (announcedPercent > 0 && announcedPercent != lastProgressAnnouncement)
            {
                lastProgressAnnouncement = announcedPercent;
                SetStatus(label + " " + announcedPercent + " por cento.");
            }
        }

        private void AnnounceStatus(string message)
        {
            SetStatus(message);
            Speak(message);
        }

        private void Speak(string message)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(message)) return;
                nvdaController_speakText(message);
            }
            catch { }
        }

        private void AnnounceForScreenReader(string message)
        {
            try
            {
                if (playerList != null)
                {
                    playerList.AccessibleDescription = message;
                    if (ActiveControl == playerList)
                    {
                        playerList.AccessibleName = "Player. " + message;
                        NotifyWinEvent(EVENT_OBJECT_NAMECHANGE, playerList.Handle, OBJID_CLIENT, CHILDID_SELF);
                    }
                }
                NotifyWinEvent(EVENT_OBJECT_NAMECHANGE, statusLabel.Handle, OBJID_CLIENT, CHILDID_SELF);
                NotifyWinEvent(EVENT_OBJECT_VALUECHANGE, statusLabel.Handle, OBJID_CLIENT, CHILDID_SELF);
            }
            catch { }
        }

        private static string GetString(Dictionary<string, object> data, string key, string fallback = "")
        {
            object value;
            if (data == null || !data.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToString(value);
        }

        private static double GetDouble(Dictionary<string, object> data, string key, double fallback = 0)
        {
            object value;
            if (data == null || !data.TryGetValue(key, out value) || value == null) return fallback;
            double parsed;
            if (Double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return fallback;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!String.IsNullOrWhiteSpace(value)) return value;
            return "";
        }

        private string ExtractJsonError(string raw)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(raw)) return "";
                var data = serializer.Deserialize<Dictionary<string, object>>(raw.Trim());
                return GetString(data, "error", "");
            }
            catch
            {
                return "";
            }
        }

        private static string FormatDuration(string raw)
        {
            double value;
            if (!Double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)) return "";
            int seconds = (int)Math.Round(value);
            int minutes = seconds / 60;
            int secs = seconds % 60;
            int hours = minutes / 60;
            minutes = minutes % 60;
            if (hours > 0) return hours + ":" + minutes.ToString("00") + ":" + secs.ToString("00");
            return minutes + ":" + secs.ToString("00");
        }

        private static string EscapeArg(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string JsonEscape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool inTextBox = ActiveControl is TextBox;
            bool inResults = IsResultsHotkeyContext();
            bool inPlayer = ActiveControl == playerList;

            if (keyData == Keys.Menu)
            {
                ShowAccessibleAltMenu();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                RequestExit();
                return true;
            }

            if (!inTextBox && playbackStarted && HandlePlayerKey(keyData))
            {
                return true;
            }

            if (keyData == customShortcuts["search"] || keyData == (Keys.Control | Keys.P))
            {
                StartMainSearch();
                return true;
            }
            if (keyData == (Keys.Alt | Keys.D2) || keyData == (Keys.Alt | Keys.NumPad2))
            {
                BrowserLogin();
                return true;
            }

            if (inResults && keyData == Keys.Enter)
            {
                PlaySelected();
                return true;
            }

            if (inResults && (keyData == Keys.Apps || keyData == (Keys.Shift | Keys.F10)))
            {
                if (resultsList.ContextMenuStrip != null)
                    resultsList.ContextMenuStrip.Show(resultsList, new Point(20, 20));
                return true;
            }
            if (inResults && keyData == Keys.Escape)
            {
                ShowHomeOnly();
                SetStatus("Resultados fechados. Pressione Alt para ir para o menu.");
                return true;
            }
            if (ActiveControl == feedList && keyData == Keys.Enter)
            {
                ExecuteFeedItem();
                return true;
            }
            if (ActiveControl == moreList && keyData == Keys.Enter)
            {
                ExecuteMoreItem();
                return true;
            }

            if (inPlayer)
            {
                if (keyData == (Keys.Control | Keys.B)) { DownloadTrackAsAudio(CurrentTrackForActions()); return true; }
                if (keyData == (Keys.Control | Keys.Shift | Keys.B)) { DownloadTrackAsVideo(CurrentTrackForActions()); return true; }
                if (HandlePlayerKey(keyData)) return true;
            }

            if (!inTextBox && IsGlobalPlayerShortcut(keyData))
            {
                if (HandlePlayerKey(keyData)) return true;
            }

            if (keyData == (Keys.Control | Keys.Right))
            {
                PlayRelative(1);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Left))
            {
                PlayRelative(-1);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "YoutubeLightAccessible_SingleInstance", out createdNew))
            {
                if (!createdNew) return;
                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
        }
    }
}













