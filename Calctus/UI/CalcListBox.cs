﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using Shapoco.Calctus.Model;
using Shapoco.Calctus.Parser;
using System.Runtime.InteropServices;
using System.Globalization;

namespace Shapoco.Calctus.UI {
    class CalcListBox : ContainerControl, ICandidateProvider {
        [DllImport("user32.dll")]
        private static extern Int32 FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO {
            public UInt32 cbSize;    // FLASHWINFO構造体のサイズ
            public IntPtr hwnd;      // 点滅対象のウィンドウ・ハンドル
            public UInt32 dwFlags;   // 以下の「FLASHW_XXX」のいずれか
            public UInt32 uCount;    // 点滅する回数
            public UInt32 dwTimeout; // 点滅する間隔（ミリ秒単位）
        }

        private const UInt32 FLASHW_STOP = 0; // 点滅を止める
        private const UInt32 FLASHW_CAPTION = 1; // タイトルバーを点滅させる
        private const UInt32 FLASHW_TRAY = 2; // タスクバー・ボタンを点滅させる
        private const UInt32 FLASHW_ALL = 3; // タスクバー・ボタンとタイトルバーを点滅させる
        private const UInt32 FLASHW_TIMER = 4; // FLASHW_STOPが指定されるまでずっと点滅させる
        private const UInt32 FLASHW_TIMERNOFG = 12; // ウィンドウが最前面に来るまでずっと点滅させる

        public const string LastAnsId = "ans";

        public event EventHandler RadixModeChanged;
        public event EventHandler DialogOpening;
        public event EventHandler DialogClosed;

        private List<CalcListItem> _items = new List<CalcListItem>();
        private int _selectedIndex = -1;
        private CalcListItem _selectedItem = null;
        private Panel _innerPanel = new Panel();
        private VScrollBar _scrollBar = new VScrollBar();
        private RadixMode _radixMode = RadixMode.Auto;

        private Candidate[] _candidates = new Candidate[0];

        private ContextMenuStrip _ctxMenu = new ContextMenuStrip();
        private ToolStripMenuItem _cmenuTextCut = new ToolStripMenuItem("Cut Text");
        private ToolStripMenuItem _cmenuTextCopy = new ToolStripMenuItem("Copy Text");
        private ToolStripMenuItem _cmenuTextPaste = new ToolStripMenuItem("Paste Text");
        private ToolStripMenuItem _cmenuTextMultilinePaste = new ToolStripMenuItem("Paste Multiline Text");
        private ToolStripMenuItem _cmenuTextDelete = new ToolStripMenuItem("Delete Text");
        private ToolStripSeparator _cmenuSep0 = new ToolStripSeparator();
        private ToolStripMenuItem _cmenuCopyAll = new ToolStripMenuItem("Copy All");
        private ToolStripSeparator _cmenuSep1 = new ToolStripSeparator();
        private ToolStripMenuItem _cmenuInsertTime = new ToolStripMenuItem("Insert Current Time");
        private ToolStripMenuItem _cmenuCreateAlarm = new ToolStripMenuItem("Create Alarm");
        private ToolStripSeparator _cmenuSep2 = new ToolStripSeparator();
        private ToolStripMenuItem _cmenuMoveUp = new ToolStripMenuItem("Move Up");
        private ToolStripMenuItem _cmenuMoveDown = new ToolStripMenuItem("Move Down");
        private ToolStripSeparator _cmenuSep3 = new ToolStripSeparator();
        private ToolStripMenuItem _cmenuItemInsert = new ToolStripMenuItem("Insert Item");
        private ToolStripMenuItem _cmenuItemDelete = new ToolStripMenuItem("Delete Item");
        private ToolStripSeparator _cmenuTextSep2 = new ToolStripSeparator();
        private ToolStripMenuItem _cmenuClear = new ToolStripMenuItem("Clear");

        private Timer _recalcTimer = new Timer();
        private bool _recalcRequested = false;
        private bool _beepingNow = false;
        private bool _beepRequested = false;

        public CalcListBox() {
            if (this.DesignMode) return;
            _scrollBar.TabStop = false;
            _scrollBar.ValueChanged += (sender, e) => { _innerPanel.Top = -((VScrollBar)_scrollBar).Value; };
            this.Controls.Add(_innerPanel);
            this.Controls.Add(_scrollBar);
            this.insert(0, new CalcListItem(this));
            this.SelectedIndex = 0;

            this.MouseUp += (sender, e) => {
                if (e.Button == MouseButtons.Right) {
                    openContextMenu(this.PointToScreen(e.Location));
                }
            };

            _cmenuTextCut.ShortcutKeyDisplayString = "Ctrl+X";
            _cmenuTextCopy.ShortcutKeyDisplayString = "Ctrl+C";
            _cmenuTextPaste.ShortcutKeyDisplayString = "Ctrl+V";
            _cmenuTextMultilinePaste.ShortcutKeyDisplayString = "Ctrl+Shift+V";
            _cmenuCopyAll.ShortcutKeyDisplayString = "Ctrl+Shift+C";
            _cmenuInsertTime.ShortcutKeyDisplayString = "Ctrl+Shift+N";
            _cmenuMoveUp.ShortcutKeyDisplayString = "Ctrl+Shift+Up";
            _cmenuMoveDown.ShortcutKeyDisplayString = "Ctrl+Shift+Down";
            _cmenuItemInsert.ShortcutKeyDisplayString = "Shift+Enter";
            _cmenuItemDelete.ShortcutKeyDisplayString = "Shift+Del";
            _cmenuClear.ShortcutKeyDisplayString = "Ctrl+Shift+Del";

            _cmenuTextCut.Click += (sender, e) => { this.SelectedItem?.OnCutText(); };
            _cmenuTextCopy.Click += (sender, e) => { this.SelectedItem?.OnCopyText(); };
            _cmenuTextPaste.Click += (sender, e) => { this.SelectedItem?.OnPasteText(); };
            _cmenuTextMultilinePaste.Click += (sender, e) => { this.MultilinePaste(); };
            _cmenuTextDelete.Click += (sender, e) => { this.SelectedItem?.OnDeleteText(); };
            _cmenuCopyAll.Click += (sender, e) => { this.CopyAll(); };
            _cmenuInsertTime.Click += (sender, e) => { this.SelectedItem?.OnInsertTime(); };
            _cmenuCreateAlarm.Click += (sender, e) => { this.CreateAlarm(); };
            _cmenuMoveUp.Click += (sender, e) => { this.ItemMoveUp(); };
            _cmenuMoveDown.Click += (sender, e) => { this.ItemMoveDown(); };
            _cmenuItemInsert.Click += (sender, e) => { this.ItemInsert(); };
            _cmenuItemDelete.Click += (sender, e) => { this.ItemDelete(); };
            _cmenuClear.Click += (sender, e) => { this.Clear(); };

            _ctxMenu.Items.AddRange(new ToolStripItem[] {
                _cmenuTextCut,
                _cmenuTextCopy,
                _cmenuTextPaste,
                _cmenuTextMultilinePaste,
                _cmenuTextDelete,
                _cmenuSep0,
                _cmenuCopyAll,
                _cmenuSep1,
                _cmenuInsertTime,
                _cmenuCreateAlarm,
                _cmenuSep2,
                _cmenuMoveUp,
                _cmenuMoveDown,
                _cmenuSep3,
                _cmenuItemInsert,
                _cmenuItemDelete,
                _cmenuTextSep2,
                _cmenuClear
            });

            _recalcTimer.Tick += _recalcTimer_Tick;
        }

        public Panel InnerPanel => _innerPanel;

        public int SelectedIndex {
            get => _selectedIndex;
            set {
                if (value == _selectedIndex) return;
                performSelectedIndexChanged(value);
            }
        }

        public CalcListItem SelectedItem {
            get => _selectedItem;
            set {
                if (value == null) this.SelectedIndex = -1;
                int i = _items.IndexOf(value);
                if (i < 0) throw new IndexOutOfRangeException();
                this.SelectedIndex = i;
            }
        }

        public RadixMode RadixMode {
            get => _radixMode;
            set {
                if (value == _radixMode) return;
                _radixMode = value;
                if (this.SelectedIndex >= 0) {
                    _items[this.SelectedIndex].RadixMode = value;
                    recalc();
                }
                RadixModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Copy() {
            var item = SelectedItem;
            if (item == null) return;
            item.OnCopyText();
        }

        public void CopyAll() {
            var sb = new StringBuilder();
            foreach(var item in _items) {
                sb.Append(item.Expression).Append(" = ").AppendLine(item.Answer);
            }
            try {
                Clipboard.Clear();
                Clipboard.SetText(sb.ToString());
            }
            catch {
                System.Media.SystemSounds.Beep.Play();
            }
        }

        public void Paste() {
            var item = SelectedItem;
            if (item == null) return;
            try {
                var text = Clipboard.GetText();
                if (text.IndexOf("\n") > 0) {
                    MultilinePaste();
                }
                else {
                    item.OnPasteText();
                }
            }
            catch { }
        }

        public void MultilinePaste() {
            int insertPos = SelectedIndex;
            if (insertPos < 0) insertPos = _items.Count;

            var dlg = new PasteOptionForm();
            DialogOpening?.Invoke(this, EventArgs.Empty);
            if (dlg.ShowDialog() == DialogResult.OK) {
                var lines = dlg.TextWillBePasted.Split('\n');
                for (int i = 0; i < lines.Length; i++) {
                    var line = lines[i].Replace("\r", "");
                    if (i == 0 && insertPos < _items.Count && string.IsNullOrEmpty(_items[insertPos].Expression)) {
                        // 先頭行については挿入先の行が空行の場合はそこを置き換える
                        _items[insertPos++].Expression = line;
                    }
                    else {
                        var item = new CalcListItem(this);
                        item.Expression = line;
                        insert(insertPos++, item);
                    }
                }
                insert(insertPos, new CalcListItem(this));
                performSelectedIndexChanged(insertPos);
                recalc();
                relayout();
            }
            dlg.Dispose();
            DialogClosed?.Invoke(this, EventArgs.Empty);
        }

        public void CreateAlarm() {
            var dlg = new CreateTimerForm();
            DialogOpening?.Invoke(this, EventArgs.Empty);
            if (dlg.ShowDialog() == DialogResult.OK) {
                var time = dlg.TimeoutTime;
                var expr = "alarm(#" + time.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) + "#)";
                InsertExpr(expr);
            }
            dlg.Dispose();
            DialogClosed?.Invoke(this, EventArgs.Empty);
        }

        public void InsertExpr(string expr) {
            var insertPos = SelectedIndex;
            if (insertPos < 0) insertPos = _items.Count;
            if (insertPos < _items.Count && string.IsNullOrEmpty(_items[insertPos].Expression)) {
                _items[insertPos].Expression = expr;
            }
            else {
                var item = new CalcListItem(this);
                item.Expression = expr;
                insert(insertPos, item);
            }
            insert(insertPos + 1, new CalcListItem(this));
            performSelectedIndexChanged(insertPos + 1);
            recalc();
            relayout();
        }

        public void ItemMoveUp() {
            var selIndex = this.SelectedIndex;
            var selItem = this.SelectedItem;
            if (selIndex < 1 || selItem == null) return;

            _items.RemoveAt(selIndex);
            _items.Insert(selIndex - 1, selItem);
            performSelectedIndexChanged(selIndex - 1);
            recalc();
            relayout();
        }

        public void ItemMoveDown() {
            var selIndex = this.SelectedIndex;
            var selItem = this.SelectedItem;
            if (selIndex >= _items.Count - 1 || selItem == null) return;

            _items.RemoveAt(selIndex);
            _items.Insert(selIndex + 1, selItem);
            performSelectedIndexChanged(selIndex + 1);
            recalc();
            relayout();
        }

        public void ItemInsert() {
            var insIndex = this.SelectedIndex;
            if (insIndex < 0) {
                insIndex = _items.Count;
            }
            insert(insIndex, new CalcListItem(this));
            performSelectedIndexChanged(insIndex);
            recalc();
            relayout();
        }

        public void ItemDelete() {
            var selIndex = this.SelectedIndex;
            var selItem = this.SelectedItem;
            if (selItem == null) return;
            delete(this.SelectedItem);
            if (_items.Count == 0) {
                insert(0, new CalcListItem(this));
            }
            if (selIndex < _items.Count) {
                performSelectedIndexChanged(selIndex);
            }
            else {
                performSelectedIndexChanged(selIndex - 1);
            }
            recalc();
            relayout();
        }

        public void Clear() {
            var ans = MessageBox.Show("Are you sure you want to delete all?", "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (ans == DialogResult.OK) {
                while (_items.Count > 0) {
                    delete(_items[_items.Count - 1]);
                }
                insert(0, new CalcListItem(this));
                performSelectedIndexChanged(0);
                recalc();
                relayout();
            }
        }

        public void Recalc() {
            recalc();
        }

        public void Refocus() {
            performSelectedIndexChanged(this.SelectedIndex);
        }

        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);
            relayout();
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            relayout();
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            if (_scrollBar.Visible) {
                int min = _scrollBar.Minimum;
                int max = _scrollBar.Maximum - _scrollBar.LargeChange;
                _scrollBar.Value = Math.Max(min, Math.Min(max, _scrollBar.Value - e.Delta));
            }
        }

        private void performSelectedIndexChanged(int newIndex) {
            bool recalc = false;
            if (_selectedItem != null) {
                recalc |= _selectedItem.IsRpnCommand(out _);
                _selectedItem.OnDeselected();
            }
            newIndex = Math.Min(_items.Count - 1, newIndex);
            _selectedIndex = newIndex;
            if (newIndex >= 0) {
                _selectedItem = _items[_selectedIndex];
                _selectedItem.OnSelected();
                showItem(_selectedIndex);
                this.RadixMode = _selectedItem.RadixMode;
                recalc |= _selectedItem.IsRpnCommand(out _);
            }
            else {
                _selectedItem = null;
            }

            if (recalc) Recalc();
        }

        private void insert(int index, CalcListItem item) {
            _items.Insert(index, item);
            _innerPanel.Controls.Add(item);
            item.ExpressionChanged += Item_ExpressionChanged;
            item.ItemKeyDown += Item_KeyDown;
            item.ItemKeyUp += Item_KeyUp;
            item.ItemGotFocus += Item_GotFocus;
            item.ItemMouseUp += Item_MouseUp;
        }

        private void delete(CalcListItem item) {
            item.ExpressionChanged -= Item_ExpressionChanged;
            item.ItemKeyDown -= Item_KeyDown;
            item.ItemKeyUp -= Item_KeyUp;
            item.ItemGotFocus -= Item_GotFocus;
            item.ItemMouseUp -= Item_MouseUp;
            _innerPanel.Controls.Remove(item);
            _items.Remove(item);
            item.Dispose();
        }

        private void Item_GotFocus(object sender, EventArgs e) {
            var item = (CalcListItem)sender;
            int index = _items.IndexOf(item);
            if (index >= 0) {
                this.SelectedIndex = index;
            }
            else {
                this.SelectedIndex = -1;
            }
        }

        private void Item_ExpressionChanged(object sender, EventArgs e) {
            relayout();
            recalc();
        }

        private void Item_KeyDown(object sender, KeyEventArgs e) {
            var item = (CalcListItem)sender;
            int index = _items.IndexOf(item);
            if (index < 0) return;
            if (e.KeyCode == Keys.Return) { // Return
                var rpn = getRpnCommand();
                if (rpn != null) {
                    // RPNコマンドの実行
                    if (rpn.Error != null) {
                        return;
                    }
                    for (int i = rpn.StartIndex; i < rpn.EndIndex; i++) {
                        delete(_items[rpn.StartIndex]);
                    }
                    _items[rpn.StartIndex].Expression = rpn.Expression;
                    index = rpn.StartIndex;
                }

                if (e.Modifiers == Keys.None) {
                    e.Handled = true;
                    if (index < _items.Count - 1) {
                        this.SelectedIndex = index + 1;
                    }
                    else {
                        var newItem = new CalcListItem(this, item);
                        this.insert(index + 1, newItem);
                        relayout();
                        this.SelectedIndex = index + 1;
                    }
                }
                else if (e.Modifiers == Keys.Shift) {
                    e.Handled = true;
                    this.insert(index, new CalcListItem(this));
                    relayout();
                    performSelectedIndexChanged(index);
                }
            }
            else if (e.KeyCode == Keys.Up && e.Modifiers == Keys.None) {
                if (this.SelectedIndex > 0) {
                    e.Handled = true;
                    this.SelectedIndex--;
                }
            }
            else if (e.KeyCode == Keys.Down && e.Modifiers == Keys.None) {
                if (this.SelectedIndex < _items.Count - 1) {
                    e.Handled = true;
                    this.SelectedIndex++;
                }
            }
            else if (e.KeyCode == Keys.Up && e.Modifiers == (Keys.Control | Keys.Shift)) {
                if (this.SelectedIndex > 0) {
                    e.Handled = true;
                    ItemMoveUp();
                }
            }
            else if (e.KeyCode == Keys.Down && e.Modifiers == (Keys.Control | Keys.Shift)) {
                if (this.SelectedIndex < _items.Count - 1) {
                    e.Handled = true;
                    ItemMoveDown();
                }
            }
            else if (e.KeyCode == Keys.PageUp && e.Modifiers == Keys.None) {
                if (this.SelectedIndex > 0) {
                    e.Handled = true;
                    pageUp();
                }
            }
            else if (e.KeyCode == Keys.PageDown && e.Modifiers == Keys.None) {
                if (this.SelectedIndex < _items.Count - 1) {
                    e.Handled = true;
                    pageDown();
                }
            }
            else if (e.KeyCode == Keys.C && e.Modifiers == (Keys.Control | Keys.Shift)) {
                e.Handled = true;
                CopyAll();
            }
            else if (e.KeyCode == Keys.V && e.Modifiers == (Keys.Control | Keys.Shift)) {
                e.Handled = true;
                MultilinePaste();
            }
            else if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.Shift) {
                e.Handled = true;
                ItemDelete();
            }
            else if (e.KeyCode == Keys.Delete && e.Modifiers == (Keys.Control | Keys.Shift)) {
                e.Handled = true;
                Clear();
            }
            else if (e.KeyCode == Keys.N && e.Modifiers == (Keys.Control | Keys.Shift)) {
                e.Handled = true;
                _cmenuInsertTime.PerformClick();
            }
        }

        private void Item_KeyUp(object sender, KeyEventArgs e) {
            var item = (CalcListItem)sender;
            if (e.KeyCode == Keys.Apps && e.Modifiers == Keys.None) {
                openContextMenu(item.PointToScreen(new Point(0, item.Height)));
            }
        }

        private void Item_MouseUp(object sender, MouseEventArgs e) {
            var item = (CalcListItem)sender;
            if (e.Button == MouseButtons.Right) {
                openContextMenu(item.PointToScreen(e.Location));
            }
        }

        private void pageUp() {
            var selItem = this.SelectedItem;
            if (selItem != null) {
                int y = selItem.Bottom - this.ClientSize.Height;
                this.SelectedIndex = indexFromClientY(y);
            }
        }

        private void pageDown() {
            var selItem = this.SelectedItem;
            if (selItem != null) {
                int y = selItem.Top + this.ClientSize.Height;
                this.SelectedIndex = indexFromClientY(y);
            }
        }

        private int indexFromClientY(int y) {
            if (_items.Count == 0) return -1;
            if (y < 0) return 0;
            for(int i = 0; i < _items.Count; i++) {
                if (y < _items[i].Bottom) {
                    return i;
                }
            }
            return _items.Count - 1;
        }

        private void openContextMenu(Point screenPos) {
            var selIndex = this.SelectedIndex;
            var selItem = this.SelectedItem;
            _cmenuTextCut.Enabled = selItem != null && selItem.IsTextCuttable;
            _cmenuTextCopy.Enabled = selItem != null && selItem.IsTextCopiable;
            _cmenuTextPaste.Enabled = selItem != null && selItem.IsTextPastable;
            _cmenuTextDelete.Enabled = selItem != null && selItem.IsTextCuttable;
            _cmenuMoveUp.Enabled = selItem != null && selIndex > 0;
            _cmenuMoveDown.Enabled = selItem != null && selIndex < _items.Count - 1;
            _cmenuItemDelete.Enabled = selItem != null;
            _ctxMenu.Show(screenPos);
        }

        private void showItem(int index) {
            if (index < 0 || index >= _items.Count) return;
            var client = this.ClientSize;
            var item = _items[index];

            if (_innerPanel.Top + item.Top < 0) {
                _scrollBar.Value = item.Top;
            }
            else if (_innerPanel.Top + item.Bottom > client.Height) {
                _scrollBar.Value = item.Bottom - client.Height;
            }
        }

        private void relayout() {
            if (_items.Count == 0) return;
            var client = this.ClientSize;
            int scrollBarWidth = _scrollBar.PreferredSize.Width;
            int itemWidth = client.Width - scrollBarWidth;
            int y = 0;
            int tabIndex = 0;
            _scrollBar.SetBounds(client.Width - scrollBarWidth, 0, scrollBarWidth, client.Height);
            foreach (var item in _items) {
                int itemHeight = item.GetPreferredSize(new Size(itemWidth, int.MaxValue)).Height;
                item.SetBounds(0, y, itemWidth, itemHeight);
                item.TabIndex = tabIndex++;
                y += itemHeight;
            }
            _innerPanel.Size = new Size(itemWidth, y);
            if (_innerPanel.Height < client.Height) {
                _scrollBar.Visible = false;
                _scrollBar.Minimum = 0;
                _scrollBar.Maximum = 0;
            }
            else {
                _scrollBar.Minimum = 0;
                _scrollBar.Maximum = Math.Max(0, _innerPanel.Height);
                _scrollBar.LargeChange = client.Height;
                _scrollBar.SmallChange = 20;
                _scrollBar.Visible = true;
            }
        }

        private void recalc() {
            EvalContext ctx = new EvalContext();

            // 設定を評価コンテキストに反映する
            var s = Settings.Instance;

            foreach (var c in s.GetUserConstants()) {
                try {
                    var expr = Parser.Parser.Parse(c.ValueString);
                    ctx.DefConst(c.Id, expr.Eval(ctx), c.Description);
                }
                catch { }
            }
            ctx.Settings.DecimalLengthToDisplay = s.NumberFormat_Decimal_MaxLen;
            ctx.Settings.ENotationEnabled = s.NumberFormat_Exp_Enabled;
            ctx.Settings.ENotationExpPositiveMin = s.NumberFormat_Exp_PositiveMin;
            ctx.Settings.ENotationExpNegativeMax = s.NumberFormat_Exp_NegativeMax;
            ctx.Settings.ENotationAlignment = s.NumberFormat_Exp_Alignment;

            var rpn = getRpnCommand();
            int rpnStart = (rpn != null) ? rpn.StartIndex : -1;
            int rpnEnd = (rpn != null) ? rpn.EndIndex : -1;

            for (int i = 0; i < _items.Count; i++) {
                var item = _items[i];
                try {
                    item.IsRpnOperand = (rpnStart <= i && i < rpnEnd);
                    if (rpnStart <= i && i < rpnEnd) {
                        item.Answer = "";
                        item.Hint = "(RPN Operand)";
                    }
                    else {
                        Expr expr = null;
                        if (rpnEnd == i && rpn != null) {
                            if (rpn.Error != null) throw rpn.Error;
                            expr = Parser.Parser.Parse(rpn.Expression);
                        }
                        else {
                            expr = Parser.Parser.Parse(item.Expression);
                        }
                        var val = expr.Eval(ctx);

                        switch (item.RadixMode) {
                            case RadixMode.Dec: val = val.FormatInt(); break;
                            case RadixMode.Hex: val = val.FormatHex(); break;
                            case RadixMode.Bin: val = val.FormatBin(); break;
                            case RadixMode.Oct: val = val.FormatOct(); break;
                        }

                        item.Answer = val.ToString(ctx);
                        item.Hint = "";
                        ctx.Ref(LastAnsId, true).Value = val;

                        item.IsHighlited = ctx.HighlightRequested;
                        ctx.ResetHighlight();
                    }
                }
                catch (Exception ex) {
                    item.Answer = "";
                    item.Hint = "? " + ex.Message;
                    item.IsHighlited = false;
                    ctx.ResetHighlight();
                    ctx.Undef(LastAnsId, true);
                }
            }

            // 再計算要求とビープ要求の対応
            if (ctx.RecalcRequested) requestRecalc1SecAfter();
            if (ctx.BeepRequested) requestBeep();

            // 計算結果をもとに入力補完候補を生成する
            ctx.Undef(LastAnsId, true);
            var list = new List<Candidate>();
            foreach (var f in FuncDef.NativeFunctions) {
                list.Add(new Candidate(f.Name, f.ToString(), f.Description, true));
            }
            foreach (var v in ctx.EnumVars()) {
                list.Add(new Candidate(v.Name.Text, v.Name.Text, v.Description, false));
            }
            list.Add(new Candidate(LastAnsId, LastAnsId, "last answer", false));
            list.Add(new Candidate(BoolVal.TrueKeyword, BoolVal.TrueKeyword, "true value", false));
            list.Add(new Candidate(BoolVal.FalseKeyword, BoolVal.FalseKeyword, "false value", false));
            _candidates = list.OrderBy(p => p.Id).ToArray();
        }

        /// <summary>RPNコマンドを解釈して RpnCommandオブジェクトを生成する</summary>
        private RpnCommand getRpnCommand() {
            int selIndex = this.SelectedIndex;
            if (selIndex < 0 || _items.Count <= selIndex) return null;

            if (_items[selIndex].IsRpnCommand(out Token[] symbols)) {
                Exception err = null;
                string expr = null;
                if (selIndex <= symbols.Length) {
                    err = new Calctus.Model.CalctusError("Invalid RPN Command");
                }

                int start = selIndex - symbols.Length - 1;
                int end = selIndex;

                try {
                    string rightStr = _items[end - 1].Expression;
                    Expr rightExpr = Parser.Parser.Parse(rightStr);
                    if (err == null) {
                        for (int i = 0; i < symbols.Length; i++) {
                            var sym = symbols[i];
                            var item = _items[end - 2 - i];
                            string leftStr = _items[end - 2 - i].Expression;
                            Expr leftExpr = Parser.Parser.Parse(leftStr);

                            var op = new BinaryOp(sym, leftExpr, rightExpr);
                            if (leftExpr is Op leftOp) {
                                if (leftOp.Method.Priority <= op.Method.Priority) {
                                    leftStr = "(" + leftStr + ")";
                                }
                            }
                            if (rightExpr is Op rightOp) {
                                if (op.Method.Priority >= rightOp.Method.Priority) {
                                    rightStr = "(" + rightStr + ")";
                                }
                            }

                            rightStr = leftStr + sym.Text + rightStr;
                            rightExpr = op;
                        }
                    }
                    expr = rightStr;
                }
                catch(Exception ex) {
                    err = ex;
                }

                Console.WriteLine(start + ", " + end + ", " + expr + ", " + err);
                return new RpnCommand(start, end, expr, err);
            }
            else {
                return null;
            }
        }

        private class RpnCommand {
            public readonly int StartIndex;
            public readonly int EndIndex;
            public readonly string Expression;
            public readonly Exception Error;
            public RpnCommand(int start, int end, string expr, Exception err) {
                StartIndex = start;
                EndIndex = end;
                Expression = expr;
                Error = err;
            }
        }

        public Candidate[] GetCandidates() => _candidates;

        private void requestRecalc1SecAfter() {
            if (!_recalcRequested) {
                _recalcRequested = true;
                _recalcTimer.Interval = 1000;
                _recalcTimer.Enabled = true;
            }
        }

        private void requestBeep() {
            if (_beepingNow) {
                if (!_beepRequested) {
                    _beepRequested = true;
                    _recalcTimer.Interval = 1000;
                    _recalcTimer.Enabled = true;
                }
            }
            else {
                beep();
            }
        }
        
        private void beep() {
            System.Media.SystemSounds.Beep.Play();
            _beepingNow = true;
            _recalcTimer.Interval = 1000;
            _recalcTimer.Enabled = true;

            // ウィンドウを点滅させる
            FLASHWINFO fInfo = new FLASHWINFO();
            fInfo.cbSize = Convert.ToUInt32(Marshal.SizeOf(fInfo));
            fInfo.hwnd = this.ParentForm.Handle;
            fInfo.dwFlags = FLASHW_ALL;
            fInfo.uCount = 1;
            fInfo.dwTimeout = 0;
            FlashWindowEx(ref fInfo);
        }

        private void _recalcTimer_Tick(object sender, EventArgs e) {
            if (!_recalcRequested && !_beepRequested) {
                _recalcTimer.Enabled = false;
            }
            _beepingNow = false;
            if (_recalcRequested) {
                _recalcRequested = false;
                recalc();
            }
            if (_beepRequested) {
                _beepRequested = false;
                beep();
            }
        }
    }

}
