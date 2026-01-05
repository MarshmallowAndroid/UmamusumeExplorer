using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Text;
using UmamusumeData;
using UmamusumeExplorer.Assets;

namespace UmamusumeExplorer.Pages
{
    partial class FileBrowserControl : UserControl
    {
        private readonly SortedDictionary<string, ManifestEntry> entries = [];
        private Dictionary<string, ManifestEntry>? searchedEntries;
        private IDictionary<string, ManifestEntry>? targetEntries;
        private readonly SortedDictionary<string, ManifestEntry> selectedEntries = [];

        private bool searched;

        public FileBrowserControl()
        {
            InitializeComponent();
        }

        private void FileBrowserControl_Load(object sender, EventArgs e)
        {
            TreeNode rootNode = new("Root");
            rootNode.Nodes.Add("");

            fileTreeView.Nodes.Add(rootNode);

            targetEntries = entries;
        }

        private void FileTreeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            Cursor = Cursors.WaitCursor;

            LoadEntries();

            TreeNode? expandingNode = e.Node;
            if (expandingNode is null) return;
            expandingNode.Nodes.Clear();

            IEnumerable<KeyValuePair<string, ManifestEntry>> entryList;

            string convertedNodePath = "";

            if (targetEntries is null) return;

            if (expandingNode.Text == "Root")
                entryList = targetEntries;
            else
            {
                convertedNodePath = expandingNode.FullPath["Root/".Length..];
                entryList = targetEntries.Where(ga => ga.Key.StartsWith(convertedNodePath + "/"));
            }

            SortedDictionary<string, ManifestEntry> files = [];

            foreach (var entryValue in entryList)
            {
                ManifestEntry entry = entryValue.Value;
                string entryName = expandingNode.Text == "Root" ? entry.Name : entry.Name[(convertedNodePath.Length + 1)..];

                int firstSlashIndex = entryName.IndexOf('/');
                if (firstSlashIndex < 1)
                    files.Add(entry.Name, entry);
                else
                {
                    string nodeName = entryName[..firstSlashIndex];
                    string nodeKey = convertedNodePath + '/' + nodeName;

                    if (!expandingNode.Nodes.ContainsKey(nodeKey))
                    {
                        expandingNode.Nodes.Add(nodeKey, nodeName);
                        expandingNode.Nodes[nodeKey]?.Nodes.Add("");
                    }
                }
            }

            foreach (var file in files.Values)
            {
                string fileName = file.BaseName;

                if (file.Manifest.StartsWith("manifest"))
                    fileName += ".manifest";

                if (!expandingNode.Nodes.ContainsKey(file.Name))
                {
                    expandingNode.Nodes.Add(file.Name, fileName);

                    TreeNode? node = expandingNode.Nodes[file.Name];

                    if (node is not null)
                    {
                        node.ImageIndex = 1;
                        node.SelectedImageIndex = 1;
                        node.Tag = file;

                        if (string.IsNullOrEmpty(UmaDataHelper.GetPath(file)))
                            node.ForeColor = Color.Red;
                    }
                }
            }

            Cursor = Cursors.Default;
        }

        private async void AddToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = fileTreeView.SelectedNode;

            if (node is null) return;

            LoadEntries();

            IEnumerable<KeyValuePair<string, ManifestEntry>> entriesToAdd = entries;

            if (node.Tag is ManifestEntry entry)
            {
                UpdateSelectedEntries(entry, node.Checked);
                entriesToAdd = selectedEntries;
            }
            else
            {
                if (targetEntries is null) return;

                if (node.FullPath == "Root")
                {
                    entriesToAdd = targetEntries;
                }
                else
                {
                    string convertedNodePath = node.FullPath["Root/".Length..];
                    entriesToAdd = targetEntries.Where(ga => ga.Key.StartsWith(convertedNodePath + '/'));
                }
            }

            int itemCount = entriesToAdd.Count();
            if (itemCount > 10000)
            {
                DialogResult confirmResult =
                    MessageBox.Show($"Adding {itemCount} items. This might take a long time.\n\nProceed?", "Warning",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirmResult == DialogResult.No) return;
            }

            await Task.Run(() =>
            {
                int currentEntry = 0;
                foreach (var item in entriesToAdd)
                {
                    Invoke(() => UpdateSelectedEntries(item.Value, node.Checked));
                    currentEntry++;

                    float percent = (float)currentEntry / itemCount;
                    Invoke(() => progressBar.Value = (int)(percent * 100.0F));
                }
            });

            if (selectedEntries is null) return;

            await Task.Run(() =>
            {
                List<ListViewItem> dependencyItems = [];
                if (SelectedEntryHasDependencies())
                {
                    DialogResult addDependencyResult = MessageBox.Show("Include dependencies?", "Dependencies found", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (addDependencyResult == DialogResult.Yes)
                    {
                        List<ManifestEntry> dependencies = [];
                        int currentEntry = 0;
                        int entryCount = selectedEntries.Count;
                        foreach (var selectedEntry in selectedEntries.Values)
                        {
                            AddDependenciesRecurse(dependencies, selectedEntry);
                            currentEntry++;

                            float percent = (float)currentEntry / entryCount;
                            Invoke(() => progressBar.Value = (int)(percent * 100.0F));
                        }

                        foreach (var dependencyEntry in dependencies)
                        {
                            Invoke(() => UpdateSelectedEntries(dependencyEntry, node.Checked));

                            ListViewItem item = new(dependencyEntry.Name);
                            item.SubItems.Add(GenerateSizeString(dependencyEntry.Length));
                            item.Tag = dependencyEntry;

                            dependencyItems.Add(item);
                        }
                    }
                }

                ListViewItem[] items = new ListViewItem[selectedEntries.Count];
                int index = 0;
                foreach (var selectedEntry in selectedEntries.Values)
                {
                    ListViewItem item = new(selectedEntry.Name);
                    item.SubItems.Add(GenerateSizeString(selectedEntry.Length));
                    item.Tag = selectedEntry;
                    items[index++] = item;
                }

                Invoke(() =>
                {
                    extractListView.Items.Clear();
                    extractListView.Items.AddRange(items);

                    totalFileCountLabel.Text = $"{selectedEntries.Count} files";
                    totalFileSizeLabel.Text = GenerateSizeString(selectedEntries.Sum(a => a.Value.Length));
                });
            });
        }

        private void RemoveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListView.SelectedListViewItemCollection selectedItems = extractListView.SelectedItems;

            foreach (ListViewItem item in selectedItems)
            {
                if (item.Tag is ManifestEntry itemTag)
                {
                    extractListView.Items.Remove(item);
                    selectedEntries?.Remove(itemTag.Name);
                }
            }

            totalFileCountLabel.Text = $"{selectedEntries?.Count} files";
            totalFileSizeLabel.Text = GenerateSizeString(selectedEntries?.Values.Sum(a => a.Length) ?? 0);
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            selectedEntries?.Clear();
            extractListView.Items.Clear();

            totalFileCountLabel.Text = $"0 files";
            totalFileSizeLabel.Text = $"0 B";
        }

        private async void ExtractButton_Click(object sender, EventArgs e)
        {
            int total = extractListView.Items.Count;

            if (total < 1) return;

            FolderBrowserDialog folderBrowserDialog = new();
            DialogResult dialogResult = folderBrowserDialog.ShowDialog();
            if (dialogResult == DialogResult.Cancel) return;

            string outputDirectory = folderBrowserDialog.SelectedPath;

            object finishedLock = new();

            Task[] copyTasks = new Task[total];
            int index = 0;
            int finished = 0;
            foreach (ListViewItem item in extractListView.Items)
            {
                if (item.Tag is not ManifestEntry entry) continue;

                copyTasks[index] = new Task(() =>
                {
                    string dataFilePath = UmaDataHelper.GetPath(entry);
                    string realFileName = entry.Name.TrimStart('/');
                    long encryptionKey = entry.EncryptionKey;
                    if (entry.Manifest.StartsWith("manifest")) realFileName += ".manifest";
                    string realFilePath = Path.Combine(outputDirectory, realFileName);
                    string destinationDirectory;

                    string? directoryName = Path.GetDirectoryName(realFileName);
                    if (!string.IsNullOrEmpty(directoryName))
                        destinationDirectory = Path.Combine(outputDirectory, directoryName);
                    else destinationDirectory = outputDirectory;

                    Directory.CreateDirectory(destinationDirectory);

                    if (!File.Exists(realFilePath) && File.Exists(dataFilePath))
                    {
                        if (encryptionKey == 0)
                        {
                            File.Copy(dataFilePath, realFilePath, true);
                        }
                        else
                        {
                            using EncryptedAssetStream assetStream = new(dataFilePath, encryptionKey);
                            using FileStream destinationStream = new(realFilePath, FileMode.Create, FileAccess.Write);
                            assetStream.CopyTo(destinationStream);
                        }
                    }

                    lock (finishedLock)
                    {
                        finished++;
                        Invoke(() => progressBar.Value = (int)((float)finished / total * 100.0F));
                    }
                });
                copyTasks[index++].Start();
            }

            await Task.WhenAll(copyTasks);

            MessageBox.Show("Extract complete.", "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            progressBar.Value = 0;
        }

        private void OpenFileLocationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fileTreeView.SelectedNode is null) return;

            if (fileTreeView.SelectedNode.Tag is ManifestEntry entry)
            {
                string dataFilePath = UmaDataHelper.GetPath(entry);

                if (!File.Exists(dataFilePath))
                {
                    MessageBox.Show("Asset does not exist locally.", "Asset not found", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }

                Process.Start("explorer.exe", $"/select, \"{dataFilePath}\"");
            }
        }

        private void TreeViewContextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            if (fileTreeView.SelectedNode is null) return;

            bool isFolder = (ManifestEntry)fileTreeView.SelectedNode.Tag is null;
            toolStripSeparator1.Visible = !isFolder;
            openFileLocationToolStripMenuItem.Visible = !isFolder;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            if (!searched)
            {
                searchedEntries = UmaDataHelper.GetManifestEntries()
                    .Where(ga => ga.Name.Contains(searchTextBox.Text))
                    .OrderBy(ga => ga.Name)
                    .ToDictionary(ga => ga.Name);
                targetEntries = searchedEntries;

                searched = true;
                searchButton.Text = "Reset";
            }
            else
            {
                searchedEntries?.Clear();
                targetEntries = entries;

                searched = false;
                searchButton.Text = "Search";
            }

            fileTreeView.Nodes[0].Collapse();
            fileTreeView.Nodes[0].Nodes.Add("");

            if (searched)
            {
                fileTreeView.ExpandAll();
                fileTreeView.Nodes[0].EnsureVisible();
            }
        }

        private void ExtractListView_Resize(object sender, EventArgs e)
        {
            extractListView.Columns[0].Width = (int)(extractListView.Width * 0.80F);
            extractListView.Columns[1].Width = -2;
        }

        private void LoadEntries()
        {
            if (entries.Count == 0)
            {
                foreach (var entry in UmaDataHelper.GetManifestEntries())
                {
                    entries.Add(entry.Name, entry);
                }
            }
        }

        private bool SelectedEntryHasDependencies()
        {
            if (selectedEntries is null) return false;

            foreach (var entry in selectedEntries.Values)
            {
                if (entry.Dependencies.Length != 0) return true;
            }

            return false;
        }

        private void AddDependenciesRecurse(List<ManifestEntry> dependencyList, ManifestEntry entry, int iteration = 0)
        {
            if (entry.Dependencies == "") return;

            foreach (var dependency in entry.Dependencies.Split(';'))
            {
                ManifestEntry? dependencyEntry = entries[dependency] ?? null;

                if (dependencyEntry is not null)
                {
                    if (iteration <= 100)
                        AddDependenciesRecurse(dependencyList, dependencyEntry, ++iteration);

                    if (!dependencyList.Contains(dependencyEntry))
                        dependencyList.Add(dependencyEntry);
                }
            }
        }

        private void UpdateSelectedEntries(ManifestEntry entry, bool isChecked)
        {
            if (selectedEntries is null) return;

            TreeNode[] matching = fileTreeView.Nodes.Find(entry.Name, true);
            if (matching.Length > 0) matching[0].Checked = isChecked;

            if (!selectedEntries.ContainsKey(entry.Name))
                selectedEntries.Add(entry.Name, entry);
        }

        private static string GenerateSizeString(long length)
        {
            StringBuilder sizeString = new();

            string[] units =
            [
                "B", "KB", "MB", "GB"
            ];

            int unitIndex = (int)Math.Floor(Math.Log(length, 10) / 3);
            unitIndex = unitIndex >= 0 ? unitIndex : 0;
            float divide = (float)Math.Pow(1000, unitIndex);
            sizeString.Append($"{length / divide:f2} {units[unitIndex]}");

            return sizeString.ToString();
        }

        private static void NodesFromPath(TreeNode? sourceNode, string path)
        {
            if (sourceNode is null) return;

            int lastSlashIndex = path.IndexOf('/');
            if (lastSlashIndex < 1)
            {
                sourceNode.Nodes.Add(path);
                return;
            }

            string nodeName = path[..lastSlashIndex];

            if (!sourceNode.Nodes.ContainsKey(nodeName))
                sourceNode.Nodes.Add(nodeName, nodeName);

            NodesFromPath(sourceNode.Nodes[nodeName], path[(lastSlashIndex + 1)..]);
        }
    }
}
