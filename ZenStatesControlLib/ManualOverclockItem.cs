using System;
using System.Windows.Forms;
using ZenStates.Core;

namespace ZenStates.Components
{
    public partial class ManualOverclockItem : UserControl
    {
        private double multi = Constants.MULTI_MIN;
        private int cores = 0;
        private int coresInCcx = 4;
        private uint vid = Constants.VID_MAX;
        private int frequency = 550;
        private int selectedCoreIndex = -1;
        private bool ocmode = false;
        private bool prochot = false;
        private Cpu.Family family = Cpu.Family.FAMILY_1AH;
        private double voltageLimit = 1.55;

        #region Private Methods
        private void PopulateFrequencyList(ComboBox.ObjectCollection l)
        {
            if (Family < Cpu.Family.FAMILY_1AH)
            {
                for (double m = Constants.MULTI_MAX; m >= Constants.MULTI_MIN; m -= Constants.MULTI_STEP)
                {
                    l.Add(new FrequencyListItem(m, string.Format("x{0:0.00}", m)));
                }
            }
        }

        private void PopulateCoreList(ComboBox.ObjectCollection l)
        {
            l.Clear();

            int coreNum = 0;

            for (int i = 0; i < Cores; ++i)
            {
                int mapIndex = i < 8 ? 0 : 1;
                if ((~coreDisableMap[mapIndex] >> i % 8 & 1) == 1)
                {
                    int ccd = i / Constants.CCD_SIZE;
                    int ccx = i / coresInCcx - CcxInCcd * ccd;
                    int core = i % coresInCcx;

                    Console.WriteLine($"ccd: {ccd}, ccx: {ccx}, core: {core}");
                    l.Add(new CoreListItem(ccd, ccx, core, string.Format("Core {0}", coreNum++)));
                }
            }

            l.Add("All Cores");
        }

        private void PopulateCCXList(ComboBox.ObjectCollection l)
        {
            l.Clear();

            for (int i = 0; i < Cores; i += coresInCcx)
            {
                int ccd = i / Constants.CCD_SIZE;
                int ccx = i / coresInCcx - CcxInCcd * ccd;

                Console.WriteLine($"ccd: {ccd}, ccx: {ccx}");
                l.Add(new CoreListItem(ccd, ccx, 0, string.Format("CCX {0}", i / coresInCcx)));
            }

            l.Add("All CCX");
        }

        private void PopulateCCDList(ComboBox.ObjectCollection l)
        {
            l.Clear();

            for (int i = 0; i < Cores; i += Constants.CCD_SIZE)
            {
                int ccd = i / Constants.CCD_SIZE;

                Console.WriteLine($"ccd: {ccd}");
                l.Add(new CoreListItem(ccd, 0, 0, string.Format("CCD {0}", ccd)));
            }

            l.Add("All CCD");
        }

        private void PopulateVidItems()
        {
            comboBoxVid.Items.Clear();
            if (family < Cpu.Family.FAMILY_19H)
            {
                for (uint i = Constants.VID_MIN; i <= Constants.VID_MAX; i++)
                {
                    CustomListItem item = new CustomListItem(i, string.Format("{0:0.000}V", Utils.VidToVoltage(i)));
                    comboBoxVid.Items.Add(item);
                    // if (i == CpuVid) comboBoxVoltage.SelectedItem = item;
                }
            }
            else
            {
                for (double i = 0.245; i <= voltageLimit; i+= 0.005)
                {
                    CustomListItem item = new CustomListItem(Utils.VoltageToVidSVI3(i), string.Format("{0:0.000}V", i));
                    comboBoxVid.Items.Add(item);
                }
            }
        }

        #endregion

        public ManualOverclockItem()
        {
            InitializeComponent();
            Reset();

            ToolTip toolTip = new ToolTip();
            toolTip.SetToolTip(comboBoxCore, "All physical cores are listed. The app can't enumerate active cores only.");
        }

        public event EventHandler SlowModeClicked;
        public event EventHandler ProchotClicked;

        #region Properties
        public uint[] coreDisableMap { get; set; }
        public int CcxInCcd { get; set; }

        public double Multi
        {
            get {
                if (Family < Cpu.Family.FAMILY_1AH)
                {
                    return (comboBoxMulti.SelectedItem as FrequencyListItem).Multi;
                }
                return 25.0;
            }
            set
            {
                multi = value;
                foreach (FrequencyListItem item in comboBoxMulti.Items)
                {
                    if (item.Multi == value)
                        comboBoxMulti.SelectedItem = item;
                }
            }
        }

        public int Cores
        {
            get => cores;
            set
            {
                cores = value;
                if (CcxInCcd > 0)
                {
                    coresInCcx = Constants.CCD_SIZE / CcxInCcd;
                }
                PopulateCoreList(comboBoxCore.Items);
                comboBoxCore.SelectedIndex = comboBoxCore.Items.Count - 1;

                if (value > 0)
                {
                    selectedCoreIndex = value;
                }
            }
        }

        public int SelectedCoreIndex
        {
            get => comboBoxCore.SelectedIndex;
        }

        public uint Vid
        {
            get => (comboBoxVid.SelectedItem as CustomListItem).Value;
            set
            {
                vid = value;
                foreach (CustomListItem item in comboBoxVid.Items)
                {
                    if (item.Value == value)
                        comboBoxVid.SelectedItem = item;
                }
            }
        }

        public int Frequency
        {
            get => Convert.ToInt32(numericUpDown.Value);
            set
            {
                if (value > 0)
                {
                    frequency = value;
                    numericUpDown.Value = Convert.ToDecimal(value);
                }
            }
        }

        public bool AllCores { get => comboBoxCore.SelectedIndex == comboBoxCore.Items.Count - 1; }
        public uint CoreMask
        {
            get
            {
                CoreListItem i = comboBoxCore.SelectedItem as CoreListItem;
                if (i == null)
                    return 0;
                // Console.WriteLine($"SET - ccd: {i.CCD}, ccx: {i.CCX }, core: {i.CORE % 4 }");
                if (Family > Cpu.Family.FAMILY_17H)
                {
                    return Convert.ToUInt32((i.CCD << 8 | i.CORE & 0xF) << 20);
                }
                return Convert.ToUInt32(((i.CCD << 4 | i.CCX % CcxInCcd & 0xF) << 4 | i.CORE % coresInCcx & 0xF) << 20);
            }
        }

        public bool OCmode
        {
            get => checkBoxOCModeEnabled.Checked;
            set
            {
                checkBoxOCModeEnabled.Checked = value;
                ocmode = value;
            }
        }

        public int ControlMode => comboBoxControlMode.SelectedIndex;

        public bool Changed
        {
            get
            {
                if (Family < Cpu.Family.FAMILY_1AH)
                {
                    return vid != Vid || multi != Multi || selectedCoreIndex != SelectedCoreIndex;
                }
                else
                {
                    return vid != Vid || frequency != Frequency || selectedCoreIndex != SelectedCoreIndex;
                }
            }
        }

        public bool ModeChanged => ocmode != OCmode;

        public bool VidChanged => vid != Vid;

        public bool ProchotEnabled
        {
            get => prochot;
            set
            {
                prochot = value;
                checkBoxProchot.Checked = value;
            }
        }

        public Cpu.Family Family
        {
            get => family;
            set
            {
                family = value;
                checkBoxSlowMode.Enabled = ocmode && Family < Cpu.Family.FAMILY_19H;
                bool vidInput = Family >= Cpu.Family.FAMILY_1AH;
                comboBoxMulti.Visible = !vidInput;
                numericUpDown.Visible = vidInput;
                Reset();
            }
        }

        public double VoltageLimit
        {
            get => voltageLimit;
            set
            {
                if (value != voltageLimit && value >= 0.245 && value <= 2.8) {
                    voltageLimit = value;
                    PopulateVidItems();
                }
            }
        }

        public void Reset()
        {
            PopulateFrequencyList(comboBoxMulti.Items);
            PopulateVidItems();

            Vid = vid;
            Frequency = frequency;
            Multi = multi;
            OCmode = ocmode;
            comboBoxCore.SelectedIndex = selectedCoreIndex;

            comboBoxCore.Enabled = OCmode;
            comboBoxMulti.Enabled = OCmode;
            comboBoxVid.Enabled = OCmode;
            numericUpDown.Enabled = OCmode;

            comboBoxControlMode.Enabled = OCmode;
            comboBoxControlMode.SelectedIndex = 0;
            checkBoxProchot.Enabled = OCmode;
            checkBoxSlowMode.Enabled = OCmode;

            checkBoxProchot.Checked = ProchotEnabled;
        }

        public void UpdateState()
        {
            vid = Vid;
            frequency = Frequency;
            multi = Multi;
            ocmode = OCmode;
            selectedCoreIndex = comboBoxCore.SelectedIndex;
        }
        #endregion

        private void CheckBoxOCModeEnabled_CheckedChanged(object sender, EventArgs e)
        {
            comboBoxCore.Enabled = OCmode;
            comboBoxMulti.Enabled = OCmode;
            comboBoxVid.Enabled = OCmode;
            numericUpDown.Enabled= OCmode;

            comboBoxControlMode.Enabled = OCmode;
            checkBoxProchot.Enabled = OCmode;
            checkBoxSlowMode.Enabled = OCmode && Family <= Cpu.Family.FAMILY_17H;
        }

        private void CheckBoxSlowMode_Click(object sender, EventArgs e)
        {
            SlowModeClicked?.Invoke(checkBoxSlowMode, e);
        }

        private void CheckBoxProchot_Click(object sender, EventArgs e)
        {
            ProchotClicked?.Invoke(checkBoxProchot, e);
        }

        private void ComboBoxControlMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            var items = comboBoxCore.Items;
            switch (comboBoxControlMode.SelectedIndex)
            {
                case 0:
                    PopulateCoreList(items);
                    break;
                case 1:
                    PopulateCCXList(items);
                    break;
                case 2:
                    PopulateCCDList(items);
                    break;
                default:
                    break;
            }

            comboBoxCore.SelectedIndex = items.Count - 1;
        }

        private void NumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            decimal step = numericUpDown.Increment;
            decimal value = numericUpDown.Value;

            numericUpDown.Value = Math.Floor(value / step) * step;
        }
    }
}
