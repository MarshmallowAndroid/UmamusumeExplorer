using System.Net.Security;
using UmamusumeData.Tables;
using UmamusumeExplorer.Assets;
using UmamusumeExplorer.Music.Live;
using Color = System.Drawing.Color;

namespace UmamusumeExplorer.Controls
{
    partial class CharacterSelectForm : Form
    {
        private readonly List<CharaData> charaDatas = [.. AssetTables.CharaDatas];
        private readonly IEnumerable<LivePermissionData> livePermissionData;
        private readonly List<int> allowedCharas = new();
        private readonly List<int> alreadySelected = new();

        public CharacterSelectForm(IEnumerable<LivePermissionData> permissionData, CharacterPosition[] characters, int currentCharacterId)
        {
            InitializeComponent();

            charaDatas.Add(new CharaData()
            {
                Id = 0
            });
            charaDatas.Sort((c1, c2) => c1.Id.CompareTo(c2.Id));
            allowedCharas.Add(0);

            livePermissionData = permissionData;
            foreach (var lpd in livePermissionData)
            {
                allowedCharas.Add(lpd.CharaId);
            }

            foreach (var character in characters)
            {
                alreadySelected.Add(character.CharacterId);
            }

            characterItemsPanel.Filter = (cd) =>
            {
                return allowedCharas.Contains(cd.Id);
            };
            characterItemsPanel.LoadingFinished += (s, e) =>
            {
                foreach (PictureBox charaIcon in characterItemsPanel.Controls)
                {
                    if (charaIcon.Tag as CharaData is not CharaData cd) return;
                    if (alreadySelected.Contains(cd.Id)) charaIcon.BackColor = Color.FromArgb(234, 54, 128);
                    if (cd.Id == currentCharacterId) charaIcon.BackColor = Color.FromArgb(247, 175, 204);
                }
            };
            characterItemsPanel.ItemClick += (s, e) =>
            {
                if (s is not PictureBox pictureBox) return;
                if (pictureBox.Tag is not CharaData cd) return;
                SelectedCharacter = cd.Id;
                Close();
            };
        }

        public int SelectedCharacter { get; private set; } = -1;

        private void CharacterSelectForm_Load(object sender, EventArgs e)
        {
            characterItemsPanel.Items = charaDatas;
        }
    }
}
