using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanCache1C
{
    class MainForm : Form
    {
        const int ColName = 0, ColFolder = 1, ColConnect = 2, ColLocal = 3, ColRoaming = 4, ColId = 5;

        readonly ComboBox cbProfile;
        readonly TextBox tbFilter;
        readonly ListView lv;
        readonly CheckBox chkRoaming;
        readonly Button btnDelete;
        readonly ToolStripStatusLabel lblStatus;
        readonly ToolStripProgressBar progress;
        readonly RowComparer comparer = new RowComparer { Column = ColLocal, Descending = true };

        List<Row> rows = new List<Row>();
        CancellationTokenSource cts;
        bool busy;

        public MainForm()
        {
            Text = "Кэш баз 1С";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1100, 620);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            // --- верхняя панель
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
            top.Controls.Add(MakeLabel("Профиль:"));
            cbProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            cbProfile.SelectedIndexChanged += (s, e) => Reload();
            top.Controls.Add(cbProfile);
            var btnRefresh = new Button { Text = "Обновить", AutoSize = true };
            btnRefresh.Click += (s, e) => Reload();
            top.Controls.Add(btnRefresh);
            top.Controls.Add(MakeLabel("   Поиск:"));
            tbFilter = new TextBox { Width = 220 };
            tbFilter.TextChanged += (s, e) => FillList();
            top.Controls.Add(tbFilter);

            // --- список
            lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                ListViewItemSorter = comparer
            };
            lv.Columns.Add("База", 260);
            lv.Columns.Add("Группа", 110);
            lv.Columns.Add("Подключение", 300);
            lv.Columns.Add("Кэш (Local)", 100, HorizontalAlignment.Right);
            lv.Columns.Add("Настройки (Roaming)", 130, HorizontalAlignment.Right);
            lv.Columns.Add("ID", 260);
            lv.ColumnClick += OnColumnClick;
            lv.ItemChecked += (s, e) => UpdateStatus();
            lv.DoubleClick += (s, e) => OpenSelected(r => r.LocalDirs);
            lv.KeyDown += OnListKeyDown;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Открыть папку кэша", null, (s, e) => OpenSelected(r => r.LocalDirs));
            menu.Items.Add("Открыть папку настроек", null, (s, e) => OpenSelected(r => r.RoamingDirs));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Копировать ID", null, (s, e) => CopySelected(r => r.Base.Id));
            menu.Items.Add("Копировать строку подключения", null, (s, e) => CopySelected(r => Scanner.MaskPassword(r.Base.Connect)));
            lv.ContextMenuStrip = menu;

            // --- нижняя панель
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6, 0, 6, 6), WrapContents = false };
            bottom.Controls.Add(MakeButton("Отметить все", (s, e) => SetChecks(r => true)));
            bottom.Controls.Add(MakeButton("Снять отметки", (s, e) => SetChecks(r => false)));
            bottom.Controls.Add(MakeButton("Отметить отсутствующие в списке", (s, e) => SetChecks(r => !r.Base.InList)));
            chkRoaming = new CheckBox { Text = "Удалять и настройки пользователя (Roaming)", AutoSize = true, Margin = new Padding(16, 7, 3, 3) };
            chkRoaming.CheckedChanged += (s, e) => UpdateStatus();
            bottom.Controls.Add(chkRoaming);
            btnDelete = MakeButton("Очистить кэш отмеченных", (s, e) => DeleteChecked());
            btnDelete.Font = new Font(Font, FontStyle.Bold);
            bottom.Controls.Add(btnDelete);

            // --- статус
            var status = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            progress = new ToolStripProgressBar { Visible = false, Width = 200 };
            status.Items.Add(lblStatus);
            status.Items.Add(progress);

            Controls.Add(lv);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(status);
            lv.BringToFront();

            Load += (s, e) =>
            {
                foreach (var p in Scanner.FindProfiles())
                    cbProfile.Items.Add(p);
                cbProfile.SelectedIndex = 0; // вызовет Reload
            };
        }

        static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        }

        static Button MakeButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += click;
            return b;
        }

        Profile CurrentProfile { get { return (Profile)cbProfile.SelectedItem; } }

        async void Reload()
        {
            if (busy || CurrentProfile == null)
                return;
            if (cts != null)
                cts.Cancel();
            cts = new CancellationTokenSource();
            var ct = cts.Token;

            var warnings = new List<string>();
            try
            {
                rows = Scanner.BuildRows(CurrentProfile, warnings);
            }
            catch (Exception ex)
            {
                rows = new List<Row>();
                warnings.Add(ex.Message);
            }
            FillList();
            if (warnings.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, warnings), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

            await ComputeSizes(rows, ct);
        }

        async Task ComputeSizes(List<Row> list, CancellationToken ct)
        {
            progress.Maximum = Math.Max(1, list.Count);
            progress.Value = 0;
            progress.Visible = true;
            try
            {
                foreach (var row in list)
                {
                    var r = row;
                    await Task.Run(() =>
                    {
                        r.LocalSize = r.LocalDirs.Sum(d => Scanner.DirSize(d, ct));
                        r.RoamingSize = r.RoamingDirs.Sum(d => Scanner.DirSize(d, ct));
                    }, ct);
                    if (ct.IsCancellationRequested)
                        return;
                    UpdateItem(r);
                    RecolorSizes();
                    progress.Value++;
                    UpdateStatus();
                }
                lv.Sort();
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (!ct.IsCancellationRequested)
                    progress.Visible = false;
            }
        }

        void FillList()
        {
            var filter = tbFilter.Text.Trim();
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var r in rows)
            {
                if (filter.Length > 0 && !Matches(r, filter))
                {
                    r.Item = null;
                    continue;
                }
                var item = new ListViewItem(new[] { r.Base.Name, r.Base.Folder, Scanner.MaskPassword(r.Base.Connect), "", "", r.Base.Id })
                {
                    Tag = r,
                    UseItemStyleForSubItems = false // чтобы красить ячейки размеров отдельно
                };
                if (!r.Base.InList)
                    foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                        sub.ForeColor = SystemColors.GrayText;
                r.Item = item;
                UpdateItem(r);
                lv.Items.Add(item);
            }
            RecolorSizes();
            lv.EndUpdate();
            UpdateStatus();
        }

        /// <summary>Тепловая раскраска ячеек размеров: зелёный — мало, жёлтый — средне, красный — больше всех.</summary>
        void RecolorSizes()
        {
            long maxLocal = rows.Max(r => (long?)r.LocalSize) ?? 0;
            long maxRoaming = rows.Max(r => (long?)r.RoamingSize) ?? 0;
            foreach (var r in rows.Where(r => r.Item != null))
            {
                r.Item.SubItems[ColLocal].BackColor = HeatColor(r.LocalSize, maxLocal);
                r.Item.SubItems[ColRoaming].BackColor = HeatColor(r.RoamingSize, maxRoaming);
            }
        }

        // пастельные цвета, как в условном форматировании Excel: текст на них читается
        static readonly Color HeatLow = Color.FromArgb(198, 239, 206);
        static readonly Color HeatMid = Color.FromArgb(255, 235, 156);
        static readonly Color HeatHigh = Color.FromArgb(255, 170, 170);

        static Color HeatColor(long size, long max)
        {
            if (size <= 0 || max <= 0)
                return SystemColors.Window;
            // корень растягивает шкалу: иначе одна огромная база делает все остальные одинаково зелёными
            double t = Math.Sqrt((double)size / max);
            return t < 0.5 ? Blend(HeatLow, HeatMid, t * 2) : Blend(HeatMid, HeatHigh, (t - 0.5) * 2);
        }

        static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        static bool Matches(Row r, string filter)
        {
            return new[] { r.Base.Name, r.Base.Folder, r.Base.Connect, r.Base.Id }
                .Any(s => s != null && s.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        static void UpdateItem(Row r)
        {
            if (r.Item == null)
                return;
            r.Item.SubItems[ColLocal].Text = SizeText(r.LocalDirs, r.LocalSize);
            r.Item.SubItems[ColRoaming].Text = SizeText(r.RoamingDirs, r.RoamingSize);
        }

        static string SizeText(List<string> dirs, long size)
        {
            if (dirs.Count == 0) return "—";
            if (size < 0) return "…";
            return Scanner.FormatSize(size);
        }

        void UpdateStatus()
        {
            long local = rows.Sum(r => Math.Max(0, r.LocalSize));
            long roaming = rows.Sum(r => Math.Max(0, r.RoamingSize));
            var check = lv.CheckedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag).ToList();
            long checkedSize = check.Sum(r => Math.Max(0, r.LocalSize) + (chkRoaming.Checked ? Math.Max(0, r.RoamingSize) : 0));

            lblStatus.Text = string.Format(
                "Баз в списке: {0}, папок без базы: {1}   |   Кэш всего: {2}, настройки: {3}   |   Отмечено: {4} ({5})",
                rows.Count(r => r.Base.InList), rows.Count(r => !r.Base.InList),
                Scanner.FormatSize(local), Scanner.FormatSize(roaming),
                check.Count, Scanner.FormatSize(checkedSize));
        }

        void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (comparer.Column == e.Column)
                comparer.Descending = !comparer.Descending;
            else
            {
                comparer.Column = e.Column;
                comparer.Descending = e.Column == ColLocal || e.Column == ColRoaming;
            }
            lv.Sort();
        }

        void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                foreach (ListViewItem item in lv.Items)
                    item.Selected = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F5)
                Reload();
        }

        void SetChecks(Func<Row, bool> predicate)
        {
            lv.BeginUpdate();
            foreach (ListViewItem item in lv.Items)
                item.Checked = predicate((Row)item.Tag);
            lv.EndUpdate();
        }

        IEnumerable<Row> SelectedRows()
        {
            return lv.SelectedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag);
        }

        void OpenSelected(Func<Row, List<string>> dirs)
        {
            var row = SelectedRows().FirstOrDefault();
            if (row == null)
                return;
            var dir = dirs(row).FirstOrDefault(System.IO.Directory.Exists);
            if (dir == null)
            {
                MessageBox.Show(this, "Папка не найдена.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        void CopySelected(Func<Row, string> value)
        {
            var text = string.Join(Environment.NewLine, SelectedRows().Select(value));
            if (text.Length > 0)
                Clipboard.SetText(text);
        }

        static int CountRunning1C(bool currentSessionOnly)
        {
            int session = Process.GetCurrentProcess().SessionId;
            int count = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.StartsWith("1cv8", StringComparison.OrdinalIgnoreCase) && (!currentSessionOnly || p.SessionId == session))
                        count++;
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return count;
        }

        async void DeleteChecked()
        {
            var targets = lv.CheckedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Отметьте галочками базы, кэш которых нужно очистить.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool withRoaming = chkRoaming.Checked;
            long bytes = targets.Sum(r => Math.Max(0, r.LocalSize) + (withRoaming ? Math.Max(0, r.RoamingSize) : 0));
            var msg = string.Format("Удалить кэш {0} баз (≈ {1})?", targets.Count, Scanner.FormatSize(bytes));
            if (withRoaming)
                msg += "\n\nНастройки пользователя (Roaming) тоже будут удалены: сохранённые формы, отборы, история и т.п.";

            // для чужого профиля не знаем владельца процесса — предупреждаем о любом запущенном клиенте 1С
            int running = CountRunning1C(CurrentProfile.IsCurrent);
            if (running > 0)
                msg += string.Format("\n\nВнимание: запущено процессов 1С: {0}. Файлы открытых баз удалить не получится — лучше сначала закрыть 1С.", running);

            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            if (cts != null)
                cts.Cancel();
            busy = true;
            UseWaitCursor = true;
            Enabled = false;
            int failed;
            try
            {
                failed = await Task.Run(() =>
                {
                    int f = 0;
                    foreach (var r in targets)
                    {
                        foreach (var d in r.LocalDirs)
                            f += Scanner.DeleteDir(d);
                        if (withRoaming)
                            foreach (var d in r.RoamingDirs)
                                f += Scanner.DeleteDir(d);
                    }
                    return f;
                });
            }
            finally
            {
                busy = false;
                Enabled = true;
                UseWaitCursor = false;
            }

            if (failed > 0)
                MessageBox.Show(this, string.Format("Не удалось удалить файлов: {0}.\nСкорее всего, они заняты запущенной 1С.", failed), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

            Reload();
        }

        class RowComparer : IComparer
        {
            public int Column;
            public bool Descending;

            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;
                var ra = (Row)a.Tag;
                var rb = (Row)b.Tag;
                int c;
                if (Column == ColLocal)
                    c = ra.LocalSize.CompareTo(rb.LocalSize);
                else if (Column == ColRoaming)
                    c = ra.RoamingSize.CompareTo(rb.RoamingSize);
                else
                    c = string.Compare(a.SubItems[Column].Text, b.SubItems[Column].Text, StringComparison.CurrentCultureIgnoreCase);
                return Descending ? -c : c;
            }
        }
    }
}
