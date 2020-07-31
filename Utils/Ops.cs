using OpenLibSys;
using System;
using System.Threading;

namespace ZenStates.Utils
{
    public class Ops : IDisposable
    {
        private Mutex hMutexPci;

        public Ops()
        {
            ols = new Ols();
            cpuType = GetCPUType(GetPkgType());
            smu = GetMaintainedSettings.GetByType(cpuType);
            smu.Version = GetSmuVersion();
            hMutexPci = new Mutex();
        }

        public SMU smu { get; }
        public Ols ols { get; }
        public SMU.CPUType cpuType { get; }

        public void CheckOlsStatus()
        {
            // Check support library status
            switch (ols.GetStatus())
            {
                case (uint)Ols.Status.NO_ERROR:
                    break;
                case (uint)Ols.Status.DLL_NOT_FOUND:
                    throw new ApplicationException("WinRing DLL_NOT_FOUND");
                case (uint)Ols.Status.DLL_INCORRECT_VERSION:
                    throw new ApplicationException("WinRing DLL_INCORRECT_VERSION");
                case (uint)Ols.Status.DLL_INITIALIZE_ERROR:
                    throw new ApplicationException("WinRing DLL_INITIALIZE_ERROR");
            }

            // Check WinRing0 status
            switch (ols.GetDllStatus())
            {
                case (uint)Ols.OlsDllStatus.OLS_DLL_NO_ERROR:
                    break;
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_NOT_LOADED:
                    throw new ApplicationException("WinRing OLS_DRIVER_NOT_LOADED");
                case (uint)Ols.OlsDllStatus.OLS_DLL_UNSUPPORTED_PLATFORM:
                    throw new ApplicationException("WinRing OLS_UNSUPPORTED_PLATFORM");
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_NOT_FOUND:
                    throw new ApplicationException("WinRing OLS_DLL_DRIVER_NOT_FOUND");
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_UNLOADED:
                    throw new ApplicationException("WinRing OLS_DLL_DRIVER_UNLOADED");
                case (uint)Ols.OlsDllStatus.OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK:
                    throw new ApplicationException("WinRing DRIVER_NOT_LOADED_ON_NETWORK");
                case (uint)Ols.OlsDllStatus.OLS_DLL_UNKNOWN_ERROR:
                    throw new ApplicationException("WinRing OLS_DLL_UNKNOWN_ERROR");
            }
        }

        public uint SetBits(uint val, int offset, int n, uint newValue)
        {
            return val & ~(((1U << n) - 1) << offset) | (newValue << offset);
        }

        public uint GetBits(uint val, int offset, int n)
        {
            return (val >> offset) & ~(~0U << n);
        }

        public bool SmuWriteReg(uint addr, uint data)
        {
            if (ols.WritePciConfigDwordEx(smu.SMU_PCI_ADDR, smu.SMU_OFFSET_ADDR, addr) == 1)
            {
                return ols.WritePciConfigDwordEx(smu.SMU_PCI_ADDR, smu.SMU_OFFSET_DATA, data) == 1;
            }
            return false;
        }

        public bool SmuReadReg(uint addr, ref uint data)
        {
            if (ols.WritePciConfigDwordEx(smu.SMU_PCI_ADDR, smu.SMU_OFFSET_ADDR, addr) == 1)
            {
                return ols.ReadPciConfigDwordEx(smu.SMU_PCI_ADDR, smu.SMU_OFFSET_DATA, ref data) == 1;
            }
            return false;
        }

        public bool SmuWaitDone()
        {
            bool res = false;
            ushort timeout = 1000;
            uint data = 0;
            while ((!res || data != 1) && --timeout > 0)
            {
                res = SmuReadReg(smu.SMU_ADDR_RSP, ref data);
            }

            if (timeout == 0 || data != 1) res = false;

            return res;
        }

        public bool SmuRead(uint msg, ref uint data)
        {
            if (SmuWriteReg(smu.SMU_ADDR_RSP, 0))
            {
                if (SmuWriteReg(smu.SMU_ADDR_MSG, msg))
                {
                    if (SmuWaitDone())
                    {
                        return SmuReadReg(smu.SMU_ADDR_ARG, ref data);
                    }
                }
            }

            return false;
        }

        public bool SmuWrite(uint msg, uint value)
        {
            bool res = false;
            // Mutex
            if (hMutexPci.WaitOne(5000))
            {
                // Clear response
                if (SmuWriteReg(smu.SMU_ADDR_RSP, 0))
                {
                    // Write data
                    if (SmuWriteReg(smu.SMU_ADDR_ARG, value))
                    {
                        SmuWriteReg(smu.SMU_ADDR_ARG + 4, 0);
                    }

                    // Send message
                    if (SmuWriteReg(smu.SMU_ADDR_MSG, msg))
                    {
                        res = SmuWaitDone();
                    }
                }
            }

            hMutexPci.ReleaseMutex();
            return res;
        }

        public uint ReadDword(uint value)
        {
            ols.WritePciConfigDword(smu.SMU_PCI_ADDR, (byte)smu.SMU_OFFSET_ADDR, value);
            return ols.ReadPciConfigDword(smu.SMU_PCI_ADDR, (byte)smu.SMU_OFFSET_DATA);
        }

        public SMU.CPUType GetCPUType(uint packageType)
        {
            SMU.CPUType cpuType;

            // CPU Check. Compare family, model, ext family, ext model. Ignore stepping/revision
            switch (GetCpuId())
            {
                case 0x00800F11: // CPU \ Zen \ Summit Ridge \ ZP - B0 \ 14nm
                case 0x00800F00: // CPU \ Zen \ Summit Ridge \ ZP - A0 \ 14nm
                    if (packageType == 7)
                        cpuType = SMU.CPUType.Threadripper;
                    else
                        cpuType = SMU.CPUType.SummitRidge;
                    break;
                case 0x00800F12:
                    cpuType = SMU.CPUType.Naples;
                    break;
                case 0x00800F82: // CPU \ Zen + \ Pinnacle Ridge \ 12nm
                    if (packageType == 7)
                        cpuType = SMU.CPUType.Colfax;
                    else
                        cpuType = SMU.CPUType.PinnacleRidge;
                    break;
                case 0x00810F81: // APU \ Zen + \ Picasso \ 12nm
                    cpuType = SMU.CPUType.Picasso;
                    break;
                case 0x00810F00: // APU \ Zen \ Raven Ridge \ RV - A0 \ 14nm
                case 0x00810F10: // APU \ Zen \ Raven Ridge \ 14nm
                case 0x00820F00: // APU \ Zen \ Raven Ridge 2 \ RV2 - A0 \ 14nm
                    cpuType = SMU.CPUType.RavenRidge;
                    break;
                case 0x00870F10: // CPU \ Zen2 \ Matisse \ MTS - B0 \ 7nm + 14nm I/ O Die
                case 0x00870F00: // CPU \ Zen2 \ Matisse \ MTS - A0 \ 7nm + 14nm I/ O Die
                    cpuType = SMU.CPUType.Matisse;
                    break;
                case 0x00830F00:
                case 0x00830F10: // CPU \ Epyc 2 \ Rome \ Treadripper 2 \ Castle Peak 7nm
                    if (packageType == 7)
                        cpuType = SMU.CPUType.Rome;
                    else
                        cpuType = SMU.CPUType.CastlePeak;
                    break;
                case 0x00850F00: // Subor Z+
                    cpuType = SMU.CPUType.Fenghuang;
                    break;
                case 0x00860F01: // APU \ Renoir
                    cpuType = SMU.CPUType.Renoir;
                    break;
                default:
                    cpuType = SMU.CPUType.Unsupported;
#if DEBUG
                    cpuType = SMU.CPUType.DEBUG;
#endif
                    break;
            }

            return cpuType;
        }

        public uint GetCpuId()
        {
            uint eax = 0, ebx = 0, ecx = 0, edx = 0;
            if (ols.Cpuid(0x00000001, ref eax, ref ebx, ref ecx, ref edx) == 1)
            {
                return eax;
            }
            return 0;
        }

        public uint GetPkgType()
        {
            uint eax = 0, ebx = 0, ecx = 0, edx = 0;
            if (ols.Cpuid(0x80000001, ref eax, ref ebx, ref ecx, ref edx) == 1)
            {
                return ebx >> 28;
            }
            return 0;
        }

        public int[] GetCoreCount()
        {
            uint eax = 0, ebx = 0, ecx = 0, edx = 0;
            uint logicalCores = 0;
            uint threadsPerCore = 1;
            int[] count = { 0, 1 };

            if (ols.Cpuid(0x00000001, ref eax, ref ebx, ref ecx, ref edx) == 1)
            {
                logicalCores = (ebx >> 16) & 0xFF;

                if (ols.Cpuid(0x8000001E, ref eax, ref ebx, ref ecx, ref edx) == 1)
                    threadsPerCore = ((ebx >> 8) & 0xF) + 1;
            }
            if (threadsPerCore != 0)
                count[0] = (int)(logicalCores / threadsPerCore);

            count[1] = (int)logicalCores;

            return count;
        }

        private string GetStringPart(uint val)
        {
            return val != 0 ? Convert.ToChar(val).ToString() : "";
        }

        private string IntToStr(uint val)
        {
            uint part1 = val & 0xff;
            uint part2 = val >> 8 & 0xff;
            uint part3 = val >> 16 & 0xff;
            uint part4 = val >> 24 & 0xff;

            return string.Format("{0}{1}{2}{3}", GetStringPart(part1), GetStringPart(part2), GetStringPart(part3), GetStringPart(part4));
        }

        public string GetCpuName()
        {
            string model = "";
            uint eax = 0, ebx = 0, ecx = 0, edx = 0;

            if (ols.Cpuid(0x80000002, ref eax, ref ebx, ref ecx, ref edx) == 1)
                model = model + IntToStr(eax) + IntToStr(ebx) + IntToStr(ecx) + IntToStr(edx);

            if (ols.Cpuid(0x80000003, ref eax, ref ebx, ref ecx, ref edx) == 1)
                model = model + IntToStr(eax) + IntToStr(ebx) + IntToStr(ecx) + IntToStr(edx);

            if (ols.Cpuid(0x80000004, ref eax, ref ebx, ref ecx, ref edx) == 1)
                model = model + IntToStr(eax) + IntToStr(ebx) + IntToStr(ecx) + IntToStr(edx);

            return model.Trim();
        }

        public uint GetSmuVersion()
        {
            uint version = 0;
            if (SmuRead(smu.SMU_MSG_GetSmuVersion, ref version))
            {
                return version;
            }
            return 0;
        }

        public uint GetPatchLevel()
        {
            uint eax = 0, edx = 0;
            if (ols.RdmsrTx(0x8b, ref eax, ref edx, (UIntPtr)(1)) != 1)
            {
                return 0;
            }
            return eax;
        }

        public bool GetOcMode()
        {
            /*
            uint eax = 0;
            uint edx = 0;

            if (ols.RdmsrTx(MSR_PStateStat, ref eax, ref edx, (UIntPtr)(1)) == 1)
            {
                // Summit Ridge, Raven Ridge
                return Convert.ToBoolean((eax >> 1) & 1);
            }
            return false;
            */
            uint scalar = 0xFF;
            if (!SmuRead(smu.SMU_MSG_GetPBOScalar, ref scalar))
            {
                return false;
            }
            return scalar == 0;
        }

        public bool IsProchotEnabled()
        {
            uint data = ReadDword(0x59804);
            return (data & 1) == 1;
        }

        public void Dispose()
        {
            hMutexPci.Dispose();
            ols.Dispose();
        }
    }
}
