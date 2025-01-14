﻿using ERC.Structures;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ERC
{
    /// <summary> Contains information needed for the associated functions relating to the process. </summary>
    public class ProcessInfo : ErcCore
    {
        #region Class_Variables
        /// <summary> Name of the process. </summary>
        public string ProcessName { get; private set; }
        /// <summary> Process description. </summary>
        public string ProcessDescription { get; private set; }
        /// <summary> Path for the projects main module. </summary>
        public string ProcessPath { get; private set; }
        /// <summary> Process ID. </summary>
        public int ProcessID { get; private set; }

        /// <summary> Handle for the process. </summary>
        public IntPtr ProcessHandle { get; private set; }
        /// <summary> .Net Process object for this process </summary>
        public Process ProcessCurrent { get; private set; }
        /// <summary> The machine type the process runs on. Eg. x86 or x64 </summary>
        public MachineType ProcessMachineType { get; private set; }
        private Dictionary<string, IntPtr> ProcessModuleHandles = new Dictionary<string, IntPtr>();
        /// <summary> A list containing ModuleInfo objects associuted with the process. /// </summary>
        public  List<ModuleInfo> ModulesInfo = new List<ModuleInfo>();
        /// <summary> A list containing ThreadInfo objects associuted with the process. /// </summary>
        public List<ThreadInfo> ThreadsInfo = new List<ThreadInfo>();

        internal ErcCore ProcessCore;
        internal List<MEMORY_BASIC_INFORMATION32> ProcessMemoryBasicInfo32;
        internal List<MEMORY_BASIC_INFORMATION64> ProcessMemoryBasicInfo64;

        private const uint LIST_MODULES_ALL = 0x03;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor for the Process_Info object, requires an ERC_Core object and a Process.
        /// </summary>
        /// <param name="core">An ErcCore object</param>
        /// <param name="process">The process to gather information from</param>
        public ProcessInfo(ErcCore core, Process process) : base(core)
        {
            ProcessCore = core;

            if (Is64Bit(process))
            {
                ProcessMachineType = MachineType.x64;
            }
            else
            {
                ProcessMachineType = MachineType.I386;
            }

            ProcessName = process.ProcessName;
            ProcessDescription = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileDescription;
            ProcessPath = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileName;
            ProcessID = process.Id;
            ProcessCurrent = process;
            ProcessHandle = process.Handle;
            ProcessModuleHandles = GetProcessModules().ReturnValue;

            if (ProcessModuleHandles.Count == 0)
            {
                for(int i = 0; i < process.Modules.Count; i++) 
                {
                    ProcessModuleHandles.Add(process.Modules[i].FileName, process.Modules[i].BaseAddress);
                }
            }
            foreach (KeyValuePair<string, IntPtr> s in ProcessModuleHandles)
            {
                ModuleInfo thisModuleInfo = new ModuleInfo(s.Key, s.Value, process, core);
                if(thisModuleInfo.ModuleFailed == false)
                {
                    ModulesInfo.Add(thisModuleInfo);
                }
            }
            for(int i = 0; i < process.Threads.Count; i++)
            {
                ThreadInfo thisThreadInfo = new ThreadInfo(process.Threads[i], ProcessCore, this);
                if(thisThreadInfo.ThreadFailed == false)
                {
                    ThreadsInfo.Add(thisThreadInfo);
                }
            }
            LocateMemoryRegions();
        }

        /// <summary>
        /// Constructor for the Process_Info object, requires an ERC_Core object and a Process.
        /// </summary>
        /// <param name="core">An ErcCore object</param>
        /// <param name="handle">The handle for the process to gather information from</param>
        public ProcessInfo(ErcCore core, IntPtr handle) : base(core)
        {
            uint flags = 0;
            bool result = GetHandleInformation(handle, out flags);
            if(result == false)
            {
                throw new ERCException("The handle provided is not a valid process (GetHandleInformation returned false)");
            }
            uint processID = GetProcessId(handle);
            
            Process process = Process.GetProcessById((int)processID);
            ProcessCore = core;

            if (Is64Bit(process))
            {
                ProcessMachineType = MachineType.x64;
            }
            else
            {
                ProcessMachineType = MachineType.I386;
            }

            ProcessName = process.ProcessName;
            ProcessDescription = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileDescription;
            ProcessPath = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileName;
            ProcessID = process.Id;
            ProcessCurrent = process;
            ProcessHandle = process.Handle;
            ProcessModuleHandles = GetProcessModules().ReturnValue;

            if (ProcessModuleHandles.Count == 0)
            {
                for (int i = 0; i < process.Modules.Count; i++)
                {
                    ProcessModuleHandles.Add(process.Modules[i].FileName, process.Modules[i].BaseAddress);
                }
            }
            foreach (KeyValuePair<string, IntPtr> s in ProcessModuleHandles)
            {
                ModuleInfo thisModuleInfo = new ModuleInfo(s.Key, s.Value, process, core);
                if (thisModuleInfo.ModuleFailed == false)
                {
                    ModulesInfo.Add(thisModuleInfo);
                }
            }
            for (int i = 0; i < process.Threads.Count; i++)
            {
                ThreadInfo thisThreadInfo = new ThreadInfo(process.Threads[i], ProcessCore, this);
                if (thisThreadInfo.ThreadFailed == false)
                {
                    ThreadsInfo.Add(thisThreadInfo);
                }
            }
            LocateMemoryRegions();
        }

        /// <summary>
        /// Constructor to use when inheriting from ProcessInfo.
        /// </summary>
        /// <param name="parent">The object to inherit from</param>
        protected ProcessInfo(ProcessInfo parent)
        {
            ProcessName = parent.ProcessName;
            ProcessDescription = parent.ProcessDescription;
            ProcessPath = parent.ProcessPath;
            ProcessID = parent.ProcessID;

            ProcessHandle = parent.ProcessHandle;
            ProcessCurrent = parent.ProcessCurrent;
            ProcessMachineType = parent.ProcessMachineType;
            ProcessModuleHandles = parent.ProcessModuleHandles;
            ModulesInfo = parent.ModulesInfo;

            ProcessCore = parent.ProcessCore;
            ProcessMemoryBasicInfo32 = parent.ProcessMemoryBasicInfo32;
            ProcessMemoryBasicInfo64 = parent.ProcessMemoryBasicInfo64;

            WorkingDirectory = parent.WorkingDirectory;
            Author = parent.Author;
        }
        #endregion

        #region ListLocalProcesses
        /// <summary>
        /// Gets a list of running processes on the host and removes unusable processes (such as system processes etc)
        /// </summary>
        /// <param name="core">An ErcCore object</param>
        /// <returns>Returns an ErcResult containing a list of all supported processes</returns>
        public static ErcResult<Process[]> ListLocalProcesses(ErcCore core)
        {
            ErcResult<Process[]> result = new ErcResult<Process[]>(core);
            Process[] processes = Process.GetProcesses();
            List<int> processesToRemove = new List<int>();

            for(int i = 0; i < processes.Length; i++)
            {
                string filename = null;
                try
                {
                    filename = processes[i].MainModule.FileName;
                }
                catch(Exception)
                {
                    processesToRemove.Add(i);
                }
            }

            Process[] usableProcesses = new Process[processes.Length - processesToRemove.Count];
            int processCounter = 0;
            for (int i = 0; i < processes.Length; i++)
            {
                if (!processesToRemove.Contains(i))
                {
                    usableProcesses[processCounter] = processes[i];
                    processCounter++;
                }
            }
            
            result.ReturnValue = usableProcesses;
            return result;
        }
        #endregion

        #region ListRemoteProcesses
        /// <summary>
        /// Gets a list of running processes on the host and removes unusable processes.
        /// </summary>
        /// <param name="core">An ErcCore object</param>
        /// <param name="machineName">The computer from which to read the list of processes. Can be either the hostname or IP address.</param>
        /// <returns>Returns an ErcResult containing a list of all supported processes</returns>
        public static ErcResult<Process[]> ListRemoteProcesses(ErcCore core, string machineName)
        {
            ErcResult<Process[]> result = new ErcResult<Process[]>(core);

            IPAddress machine = null;
            if(IPAddress.TryParse(machineName, out machine))
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(machine);
                machineName = hostEntry.HostName;
            }
            
            Process[] processes = Process.GetProcesses(machineName);
            List<int> processesToRemove = new List<int>();

            for (int i = 0; i < processes.Length; i++)
            {
                string filename = null;
                try
                {
                    filename = processes[i].MainModule.FileName;
                }
                catch (Exception)
                {
                    processesToRemove.Add(i);
                }
            }

            Process[] usableProcesses = new Process[processes.Length - processesToRemove.Count];
            int processCounter = 0;
            for (int i = 0; i < processes.Length; i++)
            {
                if (!processesToRemove.Contains(i))
                {
                    usableProcesses[processCounter] = processes[i];
                    processCounter++;
                }
            }

            result.ReturnValue = usableProcesses;
            return result;
        }
        #endregion

        #region GetProcessModules
        /// <summary>
        /// Returns a list of files loaded by the current process as List String
        /// </summary>
        /// <returns>Returns an ErcResult containing a Dictionary of module names and the associated handles</returns>
        public ErcResult<Dictionary<string, IntPtr>> GetProcessModules()
        {
            IntPtr hProcess = ProcessHandle;
            ErcResult<Dictionary<string, IntPtr>> result = new ErcResult<Dictionary<string, IntPtr>>(ProcessCore);
            result.ReturnValue = new Dictionary<string, IntPtr>();
            Dictionary<string, IntPtr> modules = new Dictionary<string, IntPtr>();
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    IntPtr[] modhWnds = new IntPtr[0];
                    int lpcbNeeded = 0;

                    try
                    {
                        // -- call EnumProcessModules the first time to get the size of the array needed
                        EnumProcessModulesEx(hProcess, modhWnds, 0, out lpcbNeeded, LIST_MODULES_ALL);

                        modhWnds = new IntPtr[lpcbNeeded / IntPtr.Size];
                        EnumProcessModulesEx(hProcess, modhWnds, modhWnds.Length * IntPtr.Size, out lpcbNeeded, LIST_MODULES_ALL);
                    }
                    catch
                    {
                        result.ReturnValue = modules;
                        return result;
                    }

                    for (int i = 0; i < modhWnds.Length; i++)
                    {
                        StringBuilder modName = new StringBuilder(256);
                        if (GetModuleFileNameEx(hProcess, modhWnds[i], modName, modName.Capacity) != 0)
                        {
                            if (!modules.ContainsKey(modName.ToString()))
                            {
                                modules.Add(modName.ToString(), modhWnds[i]);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    result.Error = e;
                    result.LogEvent();
                    return result;
                }
            }
            result.ReturnValue = modules;
            return result;
        }
        #endregion

        #region Identify_Process_Architecture
        /// <summary>
        /// Identifies if a process is 64bit or 32 bit, returns true for 64bit and false for 32bit.
        /// </summary>
        /// <param name="process">The process to be used</param>
        /// <returns>Returns true if the process is 64bit and false if it is not.</returns>
        public static bool Is64Bit(Process process)
        {
            bool isWow64;

            if(process == null)
            {
                throw new ERCException("No process attached.");
            }

            if (!Environment.Is64BitOperatingSystem)
            {
                return false;
            }

            if (!IsWow64Process(process.Handle, out isWow64))
            {
                throw new ERCException("An error has occured in the IsWow64Process call from Process.Is64Bit()");
            }

            return !isWow64;
        }
        #endregion

        #region LocateMemoryRegions
        /// <summary>
        /// Identifies memory regions occupied by the current process and populates the associated list with the Process_Info object.
        /// </summary>
        private void LocateMemoryRegions()
        {
            
            Process process = ProcessCurrent;
            if (ProcessMachineType == MachineType.I386)
            {
                ProcessMemoryBasicInfo32 = new List<MEMORY_BASIC_INFORMATION32>();
                long MaxAddress = 0x7fffffff;
                long address = 0;
                long oldAddress = 0;

                do
                {
                    MEMORY_BASIC_INFORMATION32 m;
                    int result = VirtualQueryEx32(process.Handle, (IntPtr)address, out m, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION32)));
                    if (address == (long)m.BaseAddress + (long)m.RegionSize)
                        break;
                    address = (long)m.BaseAddress + (long)m.RegionSize;
                    if (oldAddress > address)
                    {
                        address = long.MaxValue;
                    }
                    oldAddress = address;
                    if (m.State == StateEnum.MEM_COMMIT && (m.Type == TypeEnum.MEM_MAPPED || m.Type == TypeEnum.MEM_PRIVATE))
                    {
                        ProcessMemoryBasicInfo32.Add(m);
                    }
                } while (address <= MaxAddress);
            }
            else if (ProcessMachineType == MachineType.x64)
            {
                ProcessMemoryBasicInfo64 = new List<MEMORY_BASIC_INFORMATION64>();
                long MaxAddress = 0x000007FFFFFEFFFF;
                long address = 0;
                long oldAddress = 0;

                do
                {
                    MEMORY_BASIC_INFORMATION64 m;
                    int result = VirtualQueryEx64(process.Handle, (IntPtr)address, out m, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION64)));
                    if (address == (long)m.BaseAddress + (long)m.RegionSize)
                        break;
                    address = (long)m.BaseAddress + (long)m.RegionSize;
                    if (oldAddress > address)
                    {
                        address = long.MaxValue;
                    }
                    oldAddress = address;
                    if (m.State == StateEnum.MEM_COMMIT && (m.Type == TypeEnum.MEM_MAPPED || m.Type == TypeEnum.MEM_PRIVATE))
                    {
                        ProcessMemoryBasicInfo64.Add(m);
                    }

                } while (address <= MaxAddress);
            }
            else
            {
                throw new ERCException("Machine type is invalid");
            }
        }
        #endregion

        #region Search_Functions

        #region Search_Process_Memory
        /// <summary>
        /// Private function called from Search_Memory. Searches memory regions populated by the process for specific patterns.
        /// </summary>
        /// <param name="searchBytes"> Takes a byte array as input to be searched for</param>
        /// <returns>Returns a list of IntPtr for each instance found.</returns>
        internal ErcResult<List<IntPtr>> SearchProcessMemory(byte[] searchBytes)
        {
            ErcResult<List<IntPtr>> resultAddresses = new ErcResult<List<IntPtr>>(ProcessCore);

            resultAddresses.ReturnValue = new List<IntPtr>();
            Process process = ProcessCurrent;

            if (ProcessMachineType == MachineType.I386)
            {
                for (int i = 0; i < ProcessMemoryBasicInfo32.Count; i++)
                {
                    if((ulong)ProcessMemoryBasicInfo32[i].RegionSize > int.MaxValue)
                    {
                        long startAddress = (long)ProcessMemoryBasicInfo32[i].BaseAddress;
                        long endAddress = (long)ProcessMemoryBasicInfo32[i].BaseAddress + (long)(ProcessMemoryBasicInfo32[i].RegionSize - 1);
                        long region = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        for (long j = startAddress; j < endAddress; j += (region / 100))
                        {
                            byte[] buffer = new byte[region / 100]; 
                            int bytesRead = 0;
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);

                            long pos = 0;
                            long index = 0;
                            do
                            {
                                byte[] buffer1Partial = new byte[buffer.Length - pos];
                                Array.Copy(buffer, pos, buffer1Partial, 0, buffer.Length - pos);
                                index = ByteIndexOf(buffer1Partial, searchBytes);

                                if (index != -1)
                                {
                                    resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo32[i].BaseAddress + pos));
                                }
                                pos += index;
                                if (index == 0)
                                {
                                    pos += searchBytes.Length;
                                    index = 1;
                                }
                            } while (index != -1 && index != 0);
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        int bytesRead = 0;
                        IntPtr baseAddress = ProcessMemoryBasicInfo32[i].BaseAddress;
                        byte[] buffer = new byte[bufferSize]; 

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer, buffer.Length, out bytesRead);

                        long pos = 0;
                        long index = 0;
                        do
                        {
                            byte[] buffer1Partial = new byte[buffer.Length - pos];
                            Array.Copy(buffer, pos, buffer1Partial, 0, buffer.Length - pos);
                            index = ByteIndexOf(buffer1Partial, searchBytes);

                            if (index != -1)
                            {
                                resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo32[i].BaseAddress + pos));
                            }
                            pos += index;
                            if (index == 0)
                            {
                                pos += searchBytes.Length;
                                index = 1;
                            }
                        } while (index != -1 && index != 0);
                    }
                }
            }
            else if(ProcessMachineType == MachineType.x64)
            {
                byte[] buffer = new byte[int.MaxValue / 10];
                int bytesRead = 0;
                for (int i = 0; i < ProcessMemoryBasicInfo64.Count; i++)
                {
                    if (ProcessMemoryBasicInfo64[i].RegionSize > int.MaxValue)
                    {
                        ulong startAddress = ProcessMemoryBasicInfo64[i].BaseAddress;
                        ulong endAddress = ProcessMemoryBasicInfo64[i].BaseAddress + (ProcessMemoryBasicInfo64[i].RegionSize - 1);
                        ulong region = ProcessMemoryBasicInfo64[i].RegionSize;

                        for (ulong j = startAddress; j < endAddress; j += int.MaxValue / 10)
                        {
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);
                            long pos = 0;
                            long index = 0;
                            do
                            {
                                byte[] buffer1Partial = new byte[buffer.Length - pos];
                                Array.Copy(buffer, pos, buffer1Partial, 0, buffer.Length - pos);
                                index = ByteIndexOf(buffer1Partial, searchBytes);

                                if (index != -1)
                                {
                                    resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo64[i].BaseAddress + pos));
                                }
                                pos += index;
                                if (index == 0)
                                {
                                    pos += searchBytes.Length;
                                    index = 1;
                                }
                            } while (index != -1 && index != 0);
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo64[i].RegionSize;
                        bytesRead = 0;
                        IntPtr baseAddress = (IntPtr)ProcessMemoryBasicInfo64[i].BaseAddress;
                        byte[] buffer1 = new byte[bufferSize]; 

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer1, buffer1.Length, out bytesRead);
                        long pos = 0;
                        long index = 0;
                        do
                        {
                            byte[] buffer1Partial = new byte[buffer1.Length - pos];
                            Array.Copy(buffer1, pos, buffer1Partial, 0, buffer1.Length - pos);
                            index = ByteIndexOf(buffer1Partial, searchBytes);

                            if (index != -1)
                            {
                                resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo64[i].BaseAddress + pos));
                            }
                            pos += index;
                            if(index == 0)
                            {
                                pos += searchBytes.Length;
                                index = 1;
                            }
                        } while (index != -1 && index != 0);
                    }
                }
            }
            resultAddresses.ReturnValue = new HashSet<IntPtr>(resultAddresses.ReturnValue).ToList();
            return resultAddresses;  
        }

        /// <summary>
        /// Private function called from Search_Memory. Searches memory regions populated by the process for specific strings.
        /// </summary>
        /// <param name="searchBytes"> Takes a byte array as input to be searched for</param>
        /// <param name="ptrsToExclude"> Takes a byte array of values used to disqualify pointers</param>
        /// <returns>Returns a list of IntPtr for each instance found.</returns>
        internal ErcResult<List<IntPtr>> SearchProcessMemory(byte[] searchBytes, byte[] ptrsToExclude)
        {
            ErcResult<List<IntPtr>> resultAddresses = new ErcResult<List<IntPtr>>(ProcessCore);

            resultAddresses.ReturnValue = new List<IntPtr>();
            Process process = ProcessCurrent;

            if (ProcessMachineType == MachineType.I386)
            {
                for (int i = 0; i < ProcessMemoryBasicInfo32.Count; i++)
                {
                    if ((ulong)ProcessMemoryBasicInfo32[i].RegionSize > int.MaxValue)
                    {
                        long startAddress = (long)ProcessMemoryBasicInfo32[i].BaseAddress;
                        long endAddress = (long)ProcessMemoryBasicInfo32[i].BaseAddress + (long)(ProcessMemoryBasicInfo32[i].RegionSize - 1);
                        long region = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        for (long j = startAddress; j < endAddress; j += (region / 100))
                        {
                            byte[] buffer = new byte[region / 100];
                            int bytesRead = 0;
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);

                            long pos = 0;
                            long index = 0;
                            do
                            {
                                byte[] buffer1Partial = new byte[buffer.Length - pos];
                                Array.Copy(buffer, pos, buffer1Partial, 0, buffer.Length - pos);
                                index = ByteIndexOf(buffer1Partial, searchBytes);

                                if (index != -1)
                                {
                                    resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo32[i].BaseAddress + pos));
                                }
                                pos += index;
                                if (index == 0)
                                {
                                    pos += searchBytes.Length;
                                    index = 1;
                                }
                            } while (index != -1 && index != 0);
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        int bytesRead = 0;
                        IntPtr baseAddress = ProcessMemoryBasicInfo32[i].BaseAddress;
                        byte[] buffer = new byte[bufferSize];

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer, buffer.Length, out bytesRead);

                        long pos = 0;
                        long index = 0;
                        do
                        {
                            byte[] buffer1Partial = new byte[buffer.Length - pos];
                            Array.Copy(buffer, pos, buffer1Partial, 0, buffer.Length - pos);
                            index = ByteIndexOf(buffer1Partial, searchBytes);

                            if (index != -1)
                            {
                                resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo32[i].BaseAddress + pos));
                            }
                            pos += index;
                            if (index == 0)
                            {
                                pos += searchBytes.Length;
                                index = 1;
                            }
                        } while (index != -1 && index != 0);
                    }
                }
            }
            else if (ProcessMachineType == MachineType.x64)
            {
                byte[] buffer = new byte[int.MaxValue / 10];
                int bytesRead = 0;
                for (int i = 0; i < ProcessMemoryBasicInfo64.Count; i++)
                {
                    if (ProcessMemoryBasicInfo64[i].RegionSize > int.MaxValue)
                    {
                        ulong startAddress = ProcessMemoryBasicInfo64[i].BaseAddress;
                        ulong endAddress = ProcessMemoryBasicInfo64[i].BaseAddress + (ProcessMemoryBasicInfo64[i].RegionSize - 1);
                        ulong region = ProcessMemoryBasicInfo64[i].RegionSize;

                        for (ulong j = startAddress; j < endAddress; j += int.MaxValue / 10)
                        {
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);
                            long pos = 0;
                            long index = 0;
                            do
                            {
                                byte[] buffer1Partial = new byte[buffer.Length - pos];
                                Array.Copy(buffer, pos, buffer1Partial, 0, buffer.Length - pos);
                                index = ByteIndexOf(buffer1Partial, searchBytes);

                                if (index != -1)
                                {
                                    resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo64[i].BaseAddress + pos));
                                }
                                pos += index;
                                if (index == 0)
                                {
                                    pos += searchBytes.Length;
                                    index = 1;
                                }
                            } while (index != -1 && index != 0);
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo64[i].RegionSize;
                        bytesRead = 0;
                        IntPtr baseAddress = (IntPtr)ProcessMemoryBasicInfo64[i].BaseAddress;
                        byte[] buffer1 = new byte[bufferSize];

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer1, buffer1.Length, out bytesRead);
                        long pos = 0;
                        long index = 0;
                        do
                        {
                            byte[] buffer1Partial = new byte[buffer1.Length - pos];
                            Array.Copy(buffer1, pos, buffer1Partial, 0, buffer1.Length - pos);
                            index = ByteIndexOf(buffer1Partial, searchBytes);

                            if (index != -1)
                            {
                                resultAddresses.ReturnValue.Add((IntPtr)(index + (long)ProcessMemoryBasicInfo64[i].BaseAddress + pos));
                            }
                            pos += index;
                            if (index == 0)
                            {
                                pos += searchBytes.Length;
                                index = 1;
                            }
                        } while (index != -1 && index != 0);
                    }
                }
            }
            resultAddresses.ReturnValue = new HashSet<IntPtr>(resultAddresses.ReturnValue).ToList();
            resultAddresses.ReturnValue = Utilities.PtrRemover.RemovePointers(ProcessMachineType, resultAddresses.ReturnValue, ptrsToExclude);
            return resultAddresses;
        }
        #endregion

        #region SearchAllMemoryPPR
        /// <summary>
        /// Searches all memory associated with a given process and associated modules for POP X POP X RET instructions. 
        /// Passing a list of module paths or names will exclude those modules from the search. 
        /// </summary>
        /// <param name="excludes">Takes a list of module names to be excluded from the search</param>
        /// <returns>Returns an ERC_Result containing a dictionary of pointers and the main module in which they were found</returns>
        public ErcResult<Dictionary<IntPtr, string>> SearchAllMemoryPPR(List<string> excludes = null)
        {
            ErcResult<Dictionary<IntPtr, string>> ptrs = new ErcResult<Dictionary<IntPtr, string>>(ProcessCore);
            ptrs.ReturnValue = new Dictionary<IntPtr, string>();
            if (ProcessMachineType == MachineType.I386)
            {
                for (int i = 0; i < ProcessMemoryBasicInfo32.Count; i++)
                {
                    if ((ulong)ProcessMemoryBasicInfo32[i].RegionSize > int.MaxValue)
                    {
                        long start_address = (long)ProcessMemoryBasicInfo32[i].BaseAddress;
                        long end_address = (long)ProcessMemoryBasicInfo32[i].BaseAddress + (long)(ProcessMemoryBasicInfo32[i].RegionSize - 1);
                        long region = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        for (long j = start_address; j < end_address; j += (region / 100))
                        {
                            byte[] buffer = new byte[region / 100];
                            int bytesRead = 0;
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);
                            List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                            if (pprs.Count > 0)
                            {
                                for (int k = 0; k < pprs.Count; k++)
                                {
                                    if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress)))
                                    {
                                        ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress), ProcessPath);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        int bytesRead = 0;
                        IntPtr baseAddress = ProcessMemoryBasicInfo32[i].BaseAddress;
                        byte[] buffer = new byte[bufferSize];

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer, buffer.Length, out bytesRead);
                        List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                        if (pprs.Count > 0)
                        {
                            for (int k = 0; k < pprs.Count; k++)
                            {
                                if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress)))
                                {
                                    ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress), ProcessPath);
                                }
                            }
                        }
                    }
                }
            }
            else if (ProcessMachineType == MachineType.x64)
            {
                byte[] buffer = new byte[int.MaxValue / 10];
                int bytesRead = 0;
                for (int i = 0; i < ProcessMemoryBasicInfo64.Count; i++)
                {
                    if (ProcessMemoryBasicInfo64[i].RegionSize > int.MaxValue)
                    {
                        ulong startAddress = ProcessMemoryBasicInfo64[i].BaseAddress;
                        ulong endAddress = ProcessMemoryBasicInfo64[i].BaseAddress + (ProcessMemoryBasicInfo64[i].RegionSize - 1);
                        ulong region = ProcessMemoryBasicInfo64[i].RegionSize;

                        for (ulong j = startAddress; j < endAddress; j += int.MaxValue / 10)
                        {
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);
                            List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                            if (pprs.Count > 0)
                            {
                                for (int k = 0; k < pprs.Count; k++)
                                {
                                    if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress)))
                                    {
                                        ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress), ProcessPath);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo64[i].RegionSize;
                        bytesRead = 0;
                        IntPtr baseAddress = (IntPtr)ProcessMemoryBasicInfo64[i].BaseAddress;
                        byte[] buffer1 = new byte[bufferSize];

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer1, buffer1.Length, out bytesRead);
                        List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer1);
                        if(pprs.Count > 0)
                        {
                            for (int k = 0; k < pprs.Count; k++)
                            {
                                if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress)))
                                {
                                    ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress), ProcessPath);
                                }
                            }
                        }
                    }
                }
            }
            List<ModuleInfo> modules = new List<ModuleInfo>();
            for(int i = 0; i < ModulesInfo.Count; i++)
            {
                if (excludes != null)
                {
                    if (!excludes.Contains(ModulesInfo[i].ModuleName) && !excludes.Contains(ModulesInfo[i].ModulePath))
                    {
                        modules.Add(ModulesInfo[i]);
                    }
                }
                else
                {
                    modules.Add(ModulesInfo[i]);
                }
            }
            for(int i = 0; i < modules.Count; i++)
            {

                IntPtr baseAddress = modules[i].ModuleBase;
                byte[] buffer = new byte[modules[i].ModuleSize];
                int bytesread = 0;

                ReadProcessMemory(ProcessHandle, modules[i].ModuleBase, buffer, buffer.Length, out bytesread);
                List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                if (pprs.Count > 0)
                {
                    for (int k = 0; k < pprs.Count; k++)
                    {
                        if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + (ulong)modules[i].ModuleBase)))
                        {
                            ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + (ulong)modules[i].ModuleBase), modules[i].ModulePath);
                        }
                    }
                }
            }
            return ptrs;
        }

        /// <summary>
        /// Searches all memory associated with a given process and associated modules for POP X POP X RET instructions. 
        /// Passing a list of module paths or names will exclude those modules from the search. 
        /// </summary>
        /// <param name="excludes">Takes a list of module names to be excluded from the search</param>
        /// <param name="ptrsToExclude"> Takes a byte array of values used to disqualify pointers</param>
        /// <returns>Returns an ERC_Result containing a dictionary of pointers and the main module in which they were found</returns>
        public ErcResult<Dictionary<IntPtr, string>> SearchAllMemoryPPR(byte[] ptrsToExclude, List<string> excludes = null)
        {
            ErcResult<Dictionary<IntPtr, string>> ptrs = new ErcResult<Dictionary<IntPtr, string>>(ProcessCore);
            ptrs.ReturnValue = new Dictionary<IntPtr, string>();
            if (ProcessMachineType == MachineType.I386)
            {
                for (int i = 0; i < ProcessMemoryBasicInfo32.Count; i++)
                {
                    if ((ulong)ProcessMemoryBasicInfo32[i].RegionSize > int.MaxValue)
                    {
                        long start_address = (long)ProcessMemoryBasicInfo32[i].BaseAddress;
                        long end_address = (long)ProcessMemoryBasicInfo32[i].BaseAddress + (long)(ProcessMemoryBasicInfo32[i].RegionSize - 1);
                        long region = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        for (long j = start_address; j < end_address; j += (region / 100))
                        {
                            byte[] buffer = new byte[region / 100];
                            int bytesRead = 0;
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);
                            List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                            if (pprs.Count > 0)
                            {
                                for (int k = 0; k < pprs.Count; k++)
                                {
                                    if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress)))
                                    {
                                        ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress), ProcessPath);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo32[i].RegionSize;
                        int bytesRead = 0;
                        IntPtr baseAddress = ProcessMemoryBasicInfo32[i].BaseAddress;
                        byte[] buffer = new byte[bufferSize];

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer, buffer.Length, out bytesRead);
                        List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                        if (pprs.Count > 0)
                        {
                            for (int k = 0; k < pprs.Count; k++)
                            {
                                if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress)))
                                {
                                    ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + (ulong)ProcessMemoryBasicInfo32[i].BaseAddress), ProcessPath);
                                }
                            }
                        }
                    }
                }
            }
            else if (ProcessMachineType == MachineType.x64)
            {
                byte[] buffer = new byte[int.MaxValue / 10];
                int bytesRead = 0;
                for (int i = 0; i < ProcessMemoryBasicInfo64.Count; i++)
                {
                    if (ProcessMemoryBasicInfo64[i].RegionSize > int.MaxValue)
                    {
                        ulong startAddress = ProcessMemoryBasicInfo64[i].BaseAddress;
                        ulong endAddress = ProcessMemoryBasicInfo64[i].BaseAddress + (ProcessMemoryBasicInfo64[i].RegionSize - 1);
                        ulong region = ProcessMemoryBasicInfo64[i].RegionSize;

                        for (ulong j = startAddress; j < endAddress; j += int.MaxValue / 10)
                        {
                            ReadProcessMemory(ProcessHandle, (IntPtr)j, buffer, buffer.Length, out bytesRead);
                            List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                            if (pprs.Count > 0)
                            {
                                for (int k = 0; k < pprs.Count; k++)
                                {
                                    if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress)))
                                    {
                                        ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress), ProcessPath);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        long bufferSize = (long)ProcessMemoryBasicInfo64[i].RegionSize;
                        bytesRead = 0;
                        IntPtr baseAddress = (IntPtr)ProcessMemoryBasicInfo64[i].BaseAddress;
                        byte[] buffer1 = new byte[bufferSize];

                        ReadProcessMemory(ProcessHandle, baseAddress, buffer1, buffer1.Length, out bytesRead);
                        List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer1);
                        if (pprs.Count > 0)
                        {
                            for (int k = 0; k < pprs.Count; k++)
                            {
                                if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress)))
                                {
                                    ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + ProcessMemoryBasicInfo64[i].BaseAddress), ProcessPath);
                                }
                            }
                        }
                    }
                }
            }
            List<ModuleInfo> modules = new List<ModuleInfo>();
            for (int i = 0; i < ModulesInfo.Count; i++)
            {
                if (excludes != null)
                {
                    if (!excludes.Contains(ModulesInfo[i].ModuleName) && !excludes.Contains(ModulesInfo[i].ModulePath))
                    {
                        modules.Add(ModulesInfo[i]);
                    }
                }
                else
                {
                    modules.Add(ModulesInfo[i]);
                }
            }
            for (int i = 0; i < modules.Count; i++)
            {

                IntPtr baseAddress = modules[i].ModuleBase;
                byte[] buffer = new byte[modules[i].ModuleSize];
                int bytesread = 0;

                ReadProcessMemory(ProcessHandle, modules[i].ModuleBase, buffer, buffer.Length, out bytesread);
                List<int> pprs = ERC.Utilities.Payloads.PopPopRet(buffer);
                if (pprs.Count > 0)
                {
                    for (int k = 0; k < pprs.Count; k++)
                    {
                        if (!ptrs.ReturnValue.ContainsKey((IntPtr)((ulong)pprs[k] + (ulong)modules[i].ModuleBase)))
                        {
                            ptrs.ReturnValue.Add((IntPtr)((ulong)pprs[k] + (ulong)modules[i].ModuleBase), modules[i].ModulePath);
                        }
                    }
                }
            }
            ptrs.ReturnValue = Utilities.PtrRemover.RemovePointers(ProcessMachineType, ptrs.ReturnValue, ptrsToExclude);
            return ptrs;
        }
        #endregion

        #region SearchMemory
        /// <summary>
        /// Searches all memory (the process and associated DLLs) for a specific string or byte array. Strings can be passed as ASCII, Unicode, UTF7 or UTF8.
        /// Specific modules can be exclude through passing a Listof strings containing module names or paths.
        /// </summary>
        /// <param name="searchType">0 = search term is in bytes\n1 = search term is in unicode\n2 = search term is in ASCII\n3 = Search term is in UTF8\n4 = Search term is in UTF7\n5 = Search term is in UTF32</param>
        /// <param name="searchBytes">Byte array to be searched for (optional)</param>
        /// <param name="searchString">String to be searched for (optional)</param>
        /// <param name="excludes">Modules to be excluded from the search (optional)</param>
        /// <returns>Returns an ERC_Result containing pointers to all instances of the search query.</returns>
        public ErcResult<Dictionary<IntPtr, string>> SearchMemory(int searchType, byte[] searchBytes = null, string searchString = null, List<string> excludes = null)
        {
            ErcResult<Dictionary<IntPtr, string>> resultAddresses = new ErcResult<Dictionary<IntPtr, string>>(ProcessCore);
            if (searchBytes == null && searchString == null)
            {
                resultAddresses.Error = new ERCException("No search term provided. " +
                    "Either a byte array or string must be provided as the search term or there is nothing to search for.");
                resultAddresses.LogEvent();
                return resultAddresses;
            }
            resultAddresses.ReturnValue = new Dictionary<IntPtr, string>();
            switch (searchType)
            {
                case 0:
                    break;
                case 1:
                    searchBytes = Encoding.Unicode.GetBytes(searchString);
                    break;
                case 2:
                    searchBytes = Encoding.ASCII.GetBytes(searchString);
                    break;
                case 3:
                    searchBytes = Encoding.UTF8.GetBytes(searchString);
                    break;
                case 4:
                    searchBytes = Encoding.UTF7.GetBytes(searchString);
                    break;
                case 5:
                    searchBytes = Encoding.UTF32.GetBytes(searchString);
                    break;
                default:
                    resultAddresses.Error = new ERCException("Incorrect searchType value provided, value must be 0-4");
                    resultAddresses.LogEvent();
                    return resultAddresses;
            }
            var processPtrs = SearchProcessMemory(searchBytes);
            if(processPtrs.Error != null)
            {
                resultAddresses.Error = new ERCException("Error passed from Search_Process_Memory: " + processPtrs.Error.ToString());
                resultAddresses.LogEvent();
                return resultAddresses;
            }

            for(int i = 0; i < processPtrs.ReturnValue.Count; i++)
            {
                if (!resultAddresses.ReturnValue.ContainsKey(processPtrs.ReturnValue[i]))
                {
                    resultAddresses.ReturnValue.Add(processPtrs.ReturnValue[i], ProcessPath);
                }
            }

            List<ModuleInfo> modules = new List<ModuleInfo>();
            for (int i = 0; i < ModulesInfo.Count; i++)
            {
                if (excludes != null)
                {
                    if (!excludes.Contains(ModulesInfo[i].ModuleName) && !excludes.Contains(ModulesInfo[i].ModulePath))
                    {
                        modules.Add(ModulesInfo[i]);
                    }
                }
                else
                {
                    modules.Add(ModulesInfo[i]);
                }
            }
            for(int i = 0; i < modules.Count; i++)
            {
                var modulePtrs = modules[i].SearchModule(searchBytes);
                if(modulePtrs.ReturnValue.Count > 0)
                {
                    for(int j = 0; j < modulePtrs.ReturnValue.Count; j++)
                    {
                        if (!resultAddresses.ReturnValue.ContainsKey(modulePtrs.ReturnValue[j]))
                        {
                            resultAddresses.ReturnValue.Add(modulePtrs.ReturnValue[j], modules[i].ModulePath);
                        }
                    }
                }
            }
            return resultAddresses;
        }

        /// <summary>
        /// Searches all memory (the process and associated DLLs) for a specific string or byte array. Strings can be passed as ASCII, Unicode, UTF7 or UTF8.
        /// Specific modules can be exclude through passing a Listof strings containing module names or paths.
        /// </summary>
        /// <param name="searchType">0 = search term is in bytes\n1 = search term is in unicode\n2 = search term is in ASCII\n3 = Search term is in UTF8\n4 = Search term is in UTF7\n5 = Search term is in UTF32</param>
        /// <param name="ptrsToExclude"> Takes a byte array of values used to disqualify pointers</param>
        /// <param name="searchBytes">Byte array to be searched for (optional)</param>
        /// <param name="searchString">String to be searched for (optional)</param>
        /// <param name="excludes">Modules to be excluded from the search (optional)</param>
        /// <returns>Returns an ERC_Result containing pointers to all instances of the search query.</returns>
        public ErcResult<Dictionary<IntPtr, string>> SearchMemory(int searchType, byte[] ptrsToExclude, byte[] searchBytes = null, string searchString = null, List<string> excludes = null)
        {
            ErcResult<Dictionary<IntPtr, string>> resultAddresses = new ErcResult<Dictionary<IntPtr, string>>(ProcessCore);
            if (searchBytes == null && searchString == null)
            {
                resultAddresses.Error = new ERCException("No search term provided. " +
                    "Either a byte array or string must be provided as the search term or there is nothing to search for.");
                resultAddresses.LogEvent();
                return resultAddresses;
            }
            resultAddresses.ReturnValue = new Dictionary<IntPtr, string>();
            switch (searchType)
            {
                case 0:
                    break;
                case 1:
                    searchBytes = Encoding.Unicode.GetBytes(searchString);
                    break;
                case 2:
                    searchBytes = Encoding.ASCII.GetBytes(searchString);
                    break;
                case 3:
                    searchBytes = Encoding.UTF8.GetBytes(searchString);
                    break;
                case 4:
                    searchBytes = Encoding.UTF7.GetBytes(searchString);
                    break;
                case 5:
                    searchBytes = Encoding.UTF32.GetBytes(searchString);
                    break;
                default:
                    resultAddresses.Error = new ERCException("Incorrect searchType value provided, value must be 0-4");
                    resultAddresses.LogEvent();
                    return resultAddresses;
            }
            var processPtrs = SearchProcessMemory(searchBytes);
            if (processPtrs.Error != null)
            {
                resultAddresses.Error = new ERCException("Error passed from Search_Process_Memory: " + processPtrs.Error.ToString());
                resultAddresses.LogEvent();
                return resultAddresses;
            }

            for (int i = 0; i < processPtrs.ReturnValue.Count; i++)
            {
                if (!resultAddresses.ReturnValue.ContainsKey(processPtrs.ReturnValue[i]))
                {
                    resultAddresses.ReturnValue.Add(processPtrs.ReturnValue[i], ProcessPath);
                }
            }

            List<ModuleInfo> modules = new List<ModuleInfo>();
            for (int i = 0; i < ModulesInfo.Count; i++)
            {
                if (excludes != null)
                {
                    if (!excludes.Contains(ModulesInfo[i].ModuleName) && !excludes.Contains(ModulesInfo[i].ModulePath))
                    {
                        modules.Add(ModulesInfo[i]);
                    }
                }
                else
                {
                    modules.Add(ModulesInfo[i]);
                }
            }
            for (int i = 0; i < modules.Count; i++)
            {
                var modulePtrs = modules[i].SearchModule(searchBytes);
                if (modulePtrs.ReturnValue.Count > 0)
                {
                    for (int j = 0; j < modulePtrs.ReturnValue.Count; j++)
                    {
                        if (!resultAddresses.ReturnValue.ContainsKey(modulePtrs.ReturnValue[j]))
                        {
                            resultAddresses.ReturnValue.Add(modulePtrs.ReturnValue[j], modules[i].ModulePath);
                        }
                    }
                }
            }

            resultAddresses.ReturnValue = Utilities.PtrRemover.RemovePointers(ProcessMachineType, resultAddresses.ReturnValue, ptrsToExclude);
            
            return resultAddresses;
        }
        #endregion

        #region SearchModules
        /// <summary>
        /// Searches all modules loaded by a process for a specific string or byte array. Strings can be passed as ASCII, Unicode, UTF7 or UTF8.
        /// Search can be limited to specific modules through passing a List of strings containing module names or paths.
        /// </summary>
        /// <param name="searchType">0 = search term is in bytes\n1 = search term is in unicode\n2 = search term is in ASCII\n3 = Search term is in UTF8\n4 = Search term is in UTF7\n5 = Search term is in UTF32</param>
        /// <param name="ptrsToExclude"> Takes a byte array of values used to disqualify pointers</param>
        /// <param name="searchBytes">Byte array to be searched for (optional)</param>
        /// <param name="searchString">String to be searched for (optional)</param>
        /// <param name="includedModules">Modules to be included in the search (optional)</param>
        /// <param name="excludedModules">Modules to be excluded from the search (optional)</param>
        /// <returns>Returns an ERC_Result containing pointers to all instances of the search query.</returns>>
        public ErcResult<Dictionary<IntPtr, string>> SearchModules(int searchType, byte[] ptrsToExclude, byte[] searchBytes = null, string searchString = null, List<string> includedModules = null, List<string> excludedModules = null)
        {
            ErcResult<Dictionary<IntPtr, string>> resultAddresses = new ErcResult<Dictionary<IntPtr, string>>(ProcessCore);
            if (searchBytes == null && searchString == null)
            {
                resultAddresses.Error = new ERCException("No search term provided. " +
                    "Either a byte array or string must be provided as the search term or there is nothing to search for.");
                resultAddresses.LogEvent();
                return resultAddresses;
            }
            resultAddresses.ReturnValue = new Dictionary<IntPtr, string>();
            switch (searchType)
            {
                case 0:
                    break;
                case 1:
                    searchBytes = Encoding.Unicode.GetBytes(searchString);
                    break;
                case 2:
                    searchBytes = Encoding.ASCII.GetBytes(searchString);
                    break;
                case 3:
                    searchBytes = Encoding.UTF8.GetBytes(searchString);
                    break;
                case 4:
                    searchBytes = Encoding.UTF7.GetBytes(searchString);
                    break;
                case 5:
                    searchBytes = Encoding.UTF32.GetBytes(searchString);
                    break;
                default:
                    resultAddresses.Error = new ERCException("Incorrect searchType value provided, value must be 0-4");
                    resultAddresses.LogEvent();
                    return resultAddresses;
            }

            List<ModuleInfo> modules = new List<ModuleInfo>();
            for (int i = 0; i < ModulesInfo.Count; i++)
            {
                if (includedModules != null)
                {
                    if (includedModules.Contains(ModulesInfo[i].ModuleName) || includedModules.Contains(ModulesInfo[i].ModulePath))
                    {
                        if(excludedModules != null)
                        {
                            if(!excludedModules.Contains(ModulesInfo[i].ModuleName) && !excludedModules.Contains(ModulesInfo[i].ModulePath))
                            {
                                modules.Add(ModulesInfo[i]);
                            }
                        }
                    }
                }
                else
                {
                    modules.Add(ModulesInfo[i]);
                }
            }
            for (int i = 0; i < modules.Count; i++)
            {
                var modulePtrs = modules[i].SearchModule(searchBytes);
                if (modulePtrs.ReturnValue.Count > 0)
                {
                    for (int j = 0; j < modulePtrs.ReturnValue.Count; j++)
                    {
                        if (!resultAddresses.ReturnValue.ContainsKey(modulePtrs.ReturnValue[j]))
                        {
                            resultAddresses.ReturnValue.Add(modulePtrs.ReturnValue[j], modules[i].ModulePath);
                        }
                    }
                }
            }

            if(ptrsToExclude != null)
            {
                resultAddresses.ReturnValue = Utilities.PtrRemover.RemovePointers(ProcessMachineType, resultAddresses.ReturnValue, ptrsToExclude);
            }
            return resultAddresses;
        }
        #endregion

        #region FindNRP
        /// <summary>
        /// Searches process registers and identifies pointers to buffers in memory containing a non repeating pattern. Functionality to identify SEH overwrites not yet implements.
        /// </summary>
        /// <param name="searchType">(Optional) 0 = search term is system default\n1 = search term is in unicode\n2 = search term is in ASCII\n3 = Search term is in UTF8\n4 = Search term is in UTF7\n5 = Search term is in UTF32</param>
        /// <param name="extended">(Optional) Include additional characters in the pattern (!#$%^ etc) in the to be searched</param>
        /// <returns>Returns a ERC_Result containing a List of RegisterOffset</returns>
        public ErcResult<List<RegisterInfo>> FindNRP(int searchType = 0, bool extended = false)
        {
            ErcResult<List<RegisterInfo>> offsets = new ErcResult<List<RegisterInfo>>(ProcessCore);
            List<string> nrps = new List<string>();
            string pattern = "";
            if(extended == false)
            {
                pattern = File.ReadAllText(ProcessCore.PatternStandardPath);
            }
            else
            {
                pattern = File.ReadAllText(ProcessCore.PatternExtendedPath);
            }

            string nrpHolder = "";
            int counter = 0;
            for(int i = 0; i < pattern.Length; i++)
            {
                if(counter != 2)
                {
                    nrpHolder += pattern[i];
                    counter++;
                }
                else
                {
                    nrpHolder += pattern[i];
                    nrps.Add(nrpHolder);
                    nrpHolder = "";
                    counter = 0;
                }
            }

            for (int i = 0; i < ThreadsInfo.Count; i++)
            {
                var context = ThreadsInfo[i].Get_Context();
                if(context.Error != null)
                {
                    context.LogEvent();
                    offsets.Error = context.Error;
                }
            }

            List<RegisterInfo> registers = new List<RegisterInfo>();
            if(ProcessMachineType == MachineType.I386)
            {
                for (int i = 0; i < ThreadsInfo.Count; i++)
                {
                    RegisterInfo regEdi = new RegisterInfo();
                    regEdi.Register = "EDI";
                    if (ThreadsInfo[i].Context32.Edi > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Edi = ThreadsInfo[i].Context32.Edi - int.MaxValue;
                    }
                    regEdi.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Edi;
                    regEdi.ThreadID = ThreadsInfo[i].ThreadID;
                    regEdi.StringOffset = -1;
                    regEdi.RegisterOffset = -1;
                    registers.Add(regEdi);
                    RegisterInfo regEsi = new RegisterInfo();
                    regEsi.Register = "ESI";
                    if (ThreadsInfo[i].Context32.Esi > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Esi = ThreadsInfo[i].Context32.Esi - int.MaxValue;
                    }
                    regEsi.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Esi;
                    regEsi.ThreadID = ThreadsInfo[i].ThreadID;
                    regEsi.StringOffset = -1;
                    regEsi.RegisterOffset = -1;
                    registers.Add(regEsi);
                    RegisterInfo regEbx = new RegisterInfo();
                    regEbx.Register = "EBX";
                    if (ThreadsInfo[i].Context32.Ebx > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Ebx = ThreadsInfo[i].Context32.Ebx - int.MaxValue;
                    }
                    regEbx.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Ebx;
                    regEbx.ThreadID = ThreadsInfo[i].ThreadID;
                    regEbx.StringOffset = -1;
                    regEbx.RegisterOffset = -1;
                    registers.Add(regEbx);
                    RegisterInfo regEdx = new RegisterInfo();
                    regEdx.Register = "EDX";
                    if (ThreadsInfo[i].Context32.Edx > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Edx = ThreadsInfo[i].Context32.Edx - int.MaxValue;
                    }
                    regEdx.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Edx;
                    regEdx.ThreadID = ThreadsInfo[i].ThreadID;
                    regEdx.StringOffset = -1;
                    regEdx.RegisterOffset = -1;
                    registers.Add(regEdx);
                    RegisterInfo regEcx = new RegisterInfo();
                    regEcx.Register = "ECX";
                    if (ThreadsInfo[i].Context32.Ecx > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Ecx = ThreadsInfo[i].Context32.Ecx - int.MaxValue;
                    }
                    regEcx.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Ecx;
                    regEcx.ThreadID = ThreadsInfo[i].ThreadID;
                    regEcx.StringOffset = -1;
                    regEcx.RegisterOffset = -1;
                    registers.Add(regEcx);
                    RegisterInfo regEax = new RegisterInfo();
                    regEax.Register = "EAX";
                    if(ThreadsInfo[i].Context32.Eax > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Eax = ThreadsInfo[i].Context32.Eax - int.MaxValue;
                    }
                    regEax.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Eax; //Arith problem here
                    regEax.ThreadID = ThreadsInfo[i].ThreadID;
                    regEax.StringOffset = -1;
                    regEax.RegisterOffset = -1;
                    registers.Add(regEax);
                    RegisterInfo regEsp = new RegisterInfo();
                    regEsp.Register = "ESP";
                    if (ThreadsInfo[i].Context32.Esp > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Esp = ThreadsInfo[i].Context32.Esp - int.MaxValue;
                    }
                    regEsp.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Esp;
                    regEsp.ThreadID = ThreadsInfo[i].ThreadID;
                    regEsp.StringOffset = -1;
                    regEsp.RegisterOffset = -1;
                    registers.Add(regEsp);
                    RegisterInfo regEbp = new RegisterInfo();
                    regEbp.Register = "EBP";
                    if (ThreadsInfo[i].Context32.Ebp > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Ebp = ThreadsInfo[i].Context32.Ebp - int.MaxValue;
                    }
                    regEbp.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Ebp;
                    regEbp.ThreadID = ThreadsInfo[i].ThreadID;
                    regEbp.StringOffset = -1;
                    regEbp.RegisterOffset = -1;
                    registers.Add(regEbp);
                    RegisterInfo regEIP = new RegisterInfo();
                    regEIP.Register = "EIP";
                    if (ThreadsInfo[i].Context32.Eip > int.MaxValue)
                    {
                        ThreadsInfo[i].Context32.Eip = ThreadsInfo[i].Context32.Eip - int.MaxValue;
                    }
                    regEIP.RegisterValue = (IntPtr)ThreadsInfo[i].Context32.Eip;
                    regEIP.ThreadID = ThreadsInfo[i].ThreadID;
                    registers.Add(regEIP);
                }

                for (int i = 0; i < registers.Count; i++)
                {
                    for (int j = 0; j < ProcessMemoryBasicInfo32.Count; j++)
                    {
                        ulong regionStart = (ulong)ProcessMemoryBasicInfo32[j].BaseAddress;
                        ulong regionEnd = (ulong)ProcessMemoryBasicInfo32[j].BaseAddress + (ulong)ProcessMemoryBasicInfo32[j].RegionSize;

                        if (registers[i].Register != "EIP" && registers[i].Register != "EBP" &&
                            (ulong)registers[i].RegisterValue > regionStart &&
                            (ulong)registers[i].RegisterValue < regionEnd)
                        {
                            ulong bufferSize = ((ulong)ProcessMemoryBasicInfo32[j].BaseAddress + (ulong)ProcessMemoryBasicInfo32[j].RegionSize) - (ulong)registers[i].RegisterValue;
                            byte[] buffer = new byte[bufferSize];
                            int bytesRead = 0;
                            ReadProcessMemory(ProcessHandle, registers[i].RegisterValue, buffer, (int)bufferSize, out bytesRead);

                            string memoryString = "";
                            switch (searchType)
                            {
                                case 0:
                                    memoryString = Encoding.Default.GetString(buffer);
                                    break;
                                case 1:
                                    memoryString = Encoding.Unicode.GetString(buffer);
                                    break;
                                case 2:
                                    memoryString = Encoding.ASCII.GetString(buffer);
                                    break;
                                case 3:
                                    memoryString = Encoding.UTF8.GetString(buffer);
                                    break;
                                case 4:
                                    memoryString = Encoding.UTF7.GetString(buffer);
                                    break;
                                case 5:
                                    memoryString = Encoding.UTF32.GetString(buffer);
                                    break;
                                default:
                                    memoryString = Encoding.Default.GetString(buffer);
                                    break;
                            }
                            int length = -1;
                            for (int k = 0; k < nrps.Count; k++)
                            {
                                if (memoryString.Contains(nrps[k]) && pattern.Contains(nrps[k]))
                                {
                                    if (registers[i].StringOffset == -1)
                                    {
                                        registers[i].StringOffset = pattern.IndexOf(nrps[k]);
                                    }

                                    int index = memoryString.IndexOf(nrps[k]);
                                    if (registers[i].RegisterOffset == -1)
                                    {
                                        registers[i].RegisterOffset = index;
                                    }
                                    
                                    length += 3;
                                }
                            }
                            registers[i].BufferSize = length;
                        }
                        else if (registers[i].Register == "EIP")
                        {
                            string EIPValue = "";
                            switch (searchType)
                            {
                                case 0:
                                    EIPValue = Encoding.Default.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                                case 1:
                                    EIPValue = Encoding.Unicode.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                                case 2:
                                    EIPValue = Encoding.ASCII.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                                case 3:
                                    EIPValue = Encoding.UTF8.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                                case 4:
                                    EIPValue = Encoding.UTF7.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                                case 5:
                                    EIPValue = Encoding.UTF32.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                                default:
                                    EIPValue = Encoding.Default.GetString(BitConverter.GetBytes((uint)registers[i].RegisterValue));
                                    break;
                            }
                            EIPValue = EIPValue.TrimEnd(null);
                            if (pattern.Contains(EIPValue))
                            {
                                registers[i].StringOffset = pattern.IndexOf(EIPValue);
                            }
                        }
                    }
                    if (Utilities.PatternTools.PatternOffset(Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X")), ProcessCore).ReturnValue != "Value not found in pattern.")
                    {
                        if(Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X")).Length > 0)
                        {
                            string regHex = Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X"));
                            string regPos = Utilities.PatternTools.PatternOffset(Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X")), ProcessCore).ReturnValue;
                            if (!String.IsNullOrEmpty(regPos) && regPos.Any(char.IsDigit))
                            {
                                registers[i].StringOffset = Int32.Parse(Regex.Match(regPos, @"\d+").Value);
                                registers[i].overwritten = true;
                            }
                        }
                    }
                }
                for (int i = 0; i < ThreadsInfo.Count; i++)
                {
                    var pTeb = ThreadsInfo[i].PopulateTEB();
                    if (pTeb.Error == null)
                    {
                        var sehChain = ThreadsInfo[i].BuildSehChain();
                        if (sehChain.Error == null)
                        {
                            if (sehChain.ReturnValue.Count > 0)
                            {
                                for (int j = 0; j < sehChain.ReturnValue.Count; j++)
                                {
                                    string SEHValue = "";
                                    string nSEHValue = "";
                                    switch (searchType)
                                    {
                                        case 0:
                                            SEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 1:
                                            byte[] sehHolder1 = sehChain.ReturnValue[j].Item1;
                                            byte[] sehHolder2 = sehChain.ReturnValue[j].Item2;
                                            if (sehChain.ReturnValue[j].Item1[0] == 0x00)
                                            {
                                                byte[] newSEH = new byte[sehChain.ReturnValue[j].Item1.Length];
                                                Array.Copy(sehChain.ReturnValue[j].Item1, 1, newSEH, 0, sehChain.ReturnValue[j].Item1.Length - 1);
                                                newSEH[newSEH.Length - 1] = 0x00;
                                                sehHolder1 = newSEH;
                                            }
                                            if (sehChain.ReturnValue[j].Item2[0] == 0x00)
                                            {
                                                byte[] newSEH = new byte[sehChain.ReturnValue[j].Item2.Length];
                                                Array.Copy(sehChain.ReturnValue[j].Item2, 1, newSEH, 0, sehChain.ReturnValue[j].Item2.Length - 1);
                                                newSEH[newSEH.Length - 1] = 0x00;
                                                sehHolder2 = newSEH;
                                            }
                                            SEHValue = Encoding.Unicode.GetString(sehHolder1);
                                            nSEHValue = Encoding.Unicode.GetString(sehHolder2);
                                            break;
                                        case 2:
                                            SEHValue = Encoding.ASCII.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.ASCII.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 3:
                                            SEHValue = Encoding.UTF8.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.UTF8.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 4:
                                            SEHValue = Encoding.UTF7.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.UTF7.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 5:
                                            SEHValue = Encoding.UTF32.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.UTF32.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        default:
                                            SEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                    }
                                    char[] sehArray = SEHValue.ToCharArray();
                                    Array.Reverse(sehArray);
                                    string ReversedSEHValue = new string(sehArray);
                                    RegisterInfo SEH = new RegisterInfo();
                                    char[] nsehArray = nSEHValue.ToCharArray();
                                    Array.Reverse(nsehArray);
                                    string nReversedSEHValue = new string(nsehArray);
                                    string combinedSeh = SEHValue + nSEHValue;
                                    string reversedCombinedSeh = ReversedSEHValue + nReversedSEHValue;
                                    if (pattern.Contains(combinedSeh) || pattern.Contains(reversedCombinedSeh))
                                    {
                                        SEH.Register = "SEH" + i.ToString();
                                        if (pattern.Contains(reversedCombinedSeh))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(reversedCombinedSeh);
                                        }
                                        else
                                        {
                                            SEH.StringOffset = pattern.IndexOf(combinedSeh);
                                        }
                                        SEH.ThreadID = ThreadsInfo[i].ThreadID;
                                        SEH.RegisterValue = (IntPtr)BitConverter.ToInt32(sehChain.ReturnValue[j].Item1, 0);
                                        registers.Add(SEH);
                                    }
                                    else if (pattern.Contains(SEHValue) || pattern.Contains(ReversedSEHValue) || pattern.Contains(nSEHValue) || pattern.Contains(nReversedSEHValue))
                                    {
                                        SEH.Register = "SEH" + i.ToString();
                                        if (pattern.Contains(ReversedSEHValue))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(ReversedSEHValue);
                                        }
                                        else if(pattern.Contains(SEHValue))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(SEHValue);
                                        }
                                        else if(pattern.Contains(nReversedSEHValue))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(nReversedSEHValue);
                                        }
                                        else
                                        {
                                            SEH.StringOffset = pattern.IndexOf(nSEHValue);
                                        }
                                        SEH.ThreadID = ThreadsInfo[i].ThreadID;
                                        SEH.RegisterValue = (IntPtr)BitConverter.ToInt32(sehChain.ReturnValue[j].Item1, 0);
                                        registers.Add(SEH);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if(ProcessMachineType == MachineType.x64)
            {
                for (int i = 0; i < ThreadsInfo.Count; i++)
                {
                    RegisterInfo regRax = new RegisterInfo();
                    regRax.Register = "Rax";
                    regRax.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rax;
                    regRax.ThreadID = ThreadsInfo[i].ThreadID;
                    regRax.StringOffset = -1;
                    regRax.RegisterOffset = -1;
                    registers.Add(regRax);
                    RegisterInfo regRbx = new RegisterInfo();
                    regRbx.Register = "RBX";
                    regRbx.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rbx;
                    regRbx.ThreadID = ThreadsInfo[i].ThreadID;
                    regRbx.StringOffset = -1;
                    regRbx.RegisterOffset = -1;
                    registers.Add(regRbx);
                    RegisterInfo regRcx = new RegisterInfo();
                    regRcx.Register = "RCX";
                    regRcx.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rcx;
                    regRcx.ThreadID = ThreadsInfo[i].ThreadID;
                    regRcx.StringOffset = -1;
                    regRcx.RegisterOffset = -1;
                    registers.Add(regRcx);
                    RegisterInfo regRdx = new RegisterInfo();
                    regRdx.Register = "RDX";
                    regRdx.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rdx;
                    regRdx.ThreadID = ThreadsInfo[i].ThreadID;
                    regRdx.StringOffset = -1;
                    regRdx.RegisterOffset = -1;
                    registers.Add(regRdx);
                    RegisterInfo regRsp = new RegisterInfo();
                    regRsp.Register = "RSP";
                    regRsp.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rsp;
                    regRsp.ThreadID = ThreadsInfo[i].ThreadID;
                    regRsp.StringOffset = -1;
                    regRsp.RegisterOffset = -1;
                    registers.Add(regRsp);
                    RegisterInfo regRbp = new RegisterInfo();
                    regRbp.Register = "RBP";
                    regRbp.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rbp;
                    regRbp.ThreadID = ThreadsInfo[i].ThreadID;
                    regRbp.StringOffset = -1;
                    regRbp.RegisterOffset = -1;
                    registers.Add(regRbp);
                    RegisterInfo regRsi = new RegisterInfo();
                    regRsi.Register = "RSI";
                    regRsi.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rsi;
                    regRsi.ThreadID = ThreadsInfo[i].ThreadID;
                    regRsi.StringOffset = -1;
                    regRsi.RegisterOffset = -1;
                    registers.Add(regRsi);
                    RegisterInfo regRdi = new RegisterInfo();
                    regRdi.Register = "RDI";
                    regRdi.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rdi;
                    regRdi.ThreadID = ThreadsInfo[i].ThreadID;
                    regRdi.StringOffset = -1;
                    regRdi.RegisterOffset = -1;
                    registers.Add(regRdi);
                    RegisterInfo regR8 = new RegisterInfo();
                    regR8.Register = "R8";
                    regR8.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R8;
                    regR8.ThreadID = ThreadsInfo[i].ThreadID;
                    regR8.StringOffset = -1;
                    regR8.RegisterOffset = -1;
                    registers.Add(regR8);
                    RegisterInfo regR9 = new RegisterInfo();
                    regR9.Register = "R9";
                    regR9.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R9;
                    regR9.ThreadID = ThreadsInfo[i].ThreadID;
                    regR9.StringOffset = -1;
                    regR9.RegisterOffset = -1;
                    registers.Add(regR9);
                    RegisterInfo regR10 = new RegisterInfo();
                    regR10.Register = "R10";
                    regR10.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R10;
                    regR10.ThreadID = ThreadsInfo[i].ThreadID;
                    regR10.StringOffset = -1;
                    regR10.RegisterOffset = -1;
                    registers.Add(regR10);
                    RegisterInfo regR11 = new RegisterInfo();
                    regR11.Register = "R11";
                    regR11.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R11;
                    regR11.ThreadID = ThreadsInfo[i].ThreadID;
                    regR11.StringOffset = -1;
                    regR11.RegisterOffset = -1;
                    registers.Add(regR11);
                    RegisterInfo regR12 = new RegisterInfo();
                    regR12.Register = "R12";
                    regR12.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R12;
                    regR12.ThreadID = ThreadsInfo[i].ThreadID;
                    regR12.StringOffset = -1;
                    regR12.RegisterOffset = -1;
                    registers.Add(regR12);
                    RegisterInfo regR13 = new RegisterInfo();
                    regR13.Register = "R13";
                    regR13.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R13;
                    regR13.ThreadID = ThreadsInfo[i].ThreadID;
                    regR13.StringOffset = -1;
                    regR13.RegisterOffset = -1;
                    registers.Add(regR13);
                    RegisterInfo regR14 = new RegisterInfo();
                    regR14.Register = "R14";
                    regR14.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R14;
                    regR14.ThreadID = ThreadsInfo[i].ThreadID;
                    regR14.StringOffset = -1;
                    regR14.RegisterOffset = -1;
                    registers.Add(regR14);
                    RegisterInfo regR15 = new RegisterInfo();
                    regR15.Register = "R15";
                    regR15.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.R15;
                    regR15.ThreadID = ThreadsInfo[i].ThreadID;
                    regR15.StringOffset = -1;
                    regR15.RegisterOffset = -1;
                    registers.Add(regR15);
                    RegisterInfo regRIP = new RegisterInfo();
                    regRIP.Register = "RIP";
                    regRIP.RegisterValue = (IntPtr)ThreadsInfo[i].Context64.Rip;
                    regRIP.ThreadID = ThreadsInfo[i].ThreadID;
                    registers.Add(regRIP);
                }

                for (int i = 0; i < registers.Count; i++)
                {
                    for (int j = 0; j < ProcessMemoryBasicInfo64.Count; j++)
                    {
                        ulong regionStart = ProcessMemoryBasicInfo64[j].BaseAddress;
                        ulong regionEnd = ProcessMemoryBasicInfo64[j].BaseAddress + ProcessMemoryBasicInfo64[j].RegionSize;

                        if (registers[i].Register != "RIP" && registers[i].Register != "RBP" &&
                            (ulong)registers[i].RegisterValue > regionStart &&
                            (ulong)registers[i].RegisterValue < regionEnd)
                        {
                            ulong bufferSize = (ProcessMemoryBasicInfo64[j].BaseAddress + ProcessMemoryBasicInfo64[j].RegionSize) - (ulong)registers[i].RegisterValue;
                            byte[] buffer = new byte[bufferSize];
                            int bytesRead = 0;
                            ReadProcessMemory(ProcessHandle, registers[i].RegisterValue, buffer, (int)bufferSize, out bytesRead);

                            string memoryString = "";
                            switch (searchType)
                            {
                                case 0:
                                    memoryString = Encoding.Default.GetString(buffer);
                                    break;
                                case 1:
                                    memoryString = Encoding.Unicode.GetString(buffer);
                                    break;
                                case 2:
                                    memoryString = Encoding.ASCII.GetString(buffer);
                                    break;
                                case 3:
                                    memoryString = Encoding.UTF8.GetString(buffer);
                                    break;
                                case 4:
                                    memoryString = Encoding.UTF7.GetString(buffer);
                                    break;
                                case 5:
                                    memoryString = Encoding.UTF32.GetString(buffer);
                                    break;
                                default:
                                    memoryString = Encoding.Default.GetString(buffer);
                                    break;
                            }
                            int length = 0;
                            for (int k = 0; k < nrps.Count; k++)
                            {
                                if (memoryString.Contains(nrps[k]) && pattern.Contains(nrps[k]))
                                {
                                    if (registers[i].StringOffset == -1)
                                    {
                                        registers[i].StringOffset = pattern.IndexOf(nrps[k]);
                                    }

                                    int index = memoryString.IndexOf(nrps[k]);
                                    if (registers[i].RegisterOffset == -1)
                                    {
                                        registers[i].RegisterOffset = index;
                                    }

                                    length += 3;
                                }
                            }
                            registers[i].BufferSize = length;
                        }
                        else if(registers[i].Register != "RIP")
                        {
                            string RIPValue = "";
                            switch (searchType)
                            {
                                case 0:
                                    RIPValue = Encoding.Default.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                                case 1:
                                    RIPValue = Encoding.Unicode.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                                case 2:
                                    RIPValue = Encoding.ASCII.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                                case 3:
                                    RIPValue = Encoding.UTF8.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                                case 4:
                                    RIPValue = Encoding.UTF7.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                                case 5:
                                    RIPValue = Encoding.UTF32.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                                default:
                                    RIPValue = Encoding.Default.GetString(BitConverter.GetBytes((ulong)registers[i].RegisterValue));
                                    break;
                            }
                            char[] ripArray = RIPValue.ToCharArray();
                            Array.Reverse(ripArray);
                            RIPValue = new string(ripArray);
                            if (pattern.Contains(RIPValue))
                            {
                                registers[i].StringOffset = pattern.IndexOf(RIPValue);
                            }
                        }
                    }
                    if (Utilities.PatternTools.PatternOffset(Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X")), ProcessCore).ReturnValue != "Value not found in pattern.")
                    {
                        if (Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X")).Length > 0)
                        {
                            string regHex = Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X"));
                            string regPos = Utilities.PatternTools.PatternOffset(Utilities.Convert.HexToAscii(registers[i].RegisterValue.ToString("X")), ProcessCore).ReturnValue;
                            if (!String.IsNullOrEmpty(regPos) && regPos.Any(char.IsDigit))
                            {
                                registers[i].StringOffset = Int32.Parse(Regex.Match(regPos, @"\d+").Value);
                                registers[i].overwritten = true;
                            }
                        }
                    }
                }
                for(int i = 0; i < ThreadsInfo.Count; i++)
                {
                    var pTeb = ThreadsInfo[i].PopulateTEB();
                    if(pTeb.Error == null)
                    {
                        var sehChain = ThreadsInfo[i].BuildSehChain();
                        if(sehChain.Error == null)
                        {
                            if(sehChain.ReturnValue.Count > 0)
                            {
                                for(int j = 0; j < sehChain.ReturnValue.Count; j++)
                                {
                                    string SEHValue = "";
                                    string nSEHValue = "";
                                    switch (searchType)
                                    {
                                        case 0:
                                            SEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 1:
                                            byte[] sehHolder1 = sehChain.ReturnValue[j].Item1;
                                            byte[] sehHolder2 = sehChain.ReturnValue[j].Item2;
                                            if (sehChain.ReturnValue[j].Item1[0] == 0x00)
                                            {
                                                byte[] newSEH = new byte[sehChain.ReturnValue[j].Item1.Length];
                                                Array.Copy(sehChain.ReturnValue[j].Item1, 1, newSEH, 0, sehChain.ReturnValue[j].Item1.Length - 1);
                                                newSEH[newSEH.Length - 1] = 0x00;
                                                sehHolder1 = newSEH;
                                            }
                                            if (sehChain.ReturnValue[j].Item2[0] == 0x00)
                                            {
                                                byte[] newSEH = new byte[sehChain.ReturnValue[j].Item2.Length];
                                                Array.Copy(sehChain.ReturnValue[j].Item2, 1, newSEH, 0, sehChain.ReturnValue[j].Item2.Length - 1);
                                                newSEH[newSEH.Length - 1] = 0x00;
                                                sehHolder2 = newSEH;
                                            }
                                            SEHValue = Encoding.Unicode.GetString(sehHolder1);
                                            nSEHValue = Encoding.Unicode.GetString(sehHolder2);
                                            break;
                                        case 2:
                                            SEHValue = Encoding.ASCII.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.ASCII.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 3:
                                            SEHValue = Encoding.UTF8.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.UTF8.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 4:
                                            SEHValue = Encoding.UTF7.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.UTF7.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        case 5:
                                            SEHValue = Encoding.UTF32.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.UTF32.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                        default:
                                            SEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item1);
                                            nSEHValue = Encoding.Default.GetString(sehChain.ReturnValue[j].Item2);
                                            break;
                                    }
                                    char[] sehArray = SEHValue.ToCharArray();
                                    Array.Reverse(sehArray);
                                    string ReversedSEHValue = new string(sehArray);
                                    RegisterInfo SEH = new RegisterInfo();
                                    char[] nsehArray = nSEHValue.ToCharArray();
                                    Array.Reverse(nsehArray);
                                    string nReversedSEHValue = new string(nsehArray);
                                    string combinedSeh = SEHValue + nSEHValue;
                                    string reversedCombinedSeh = ReversedSEHValue + nReversedSEHValue;
                                    if (pattern.Contains(combinedSeh) || pattern.Contains(reversedCombinedSeh))
                                    {
                                        SEH.Register = "SEH" + i.ToString();
                                        if (pattern.Contains(reversedCombinedSeh))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(reversedCombinedSeh);
                                        }
                                        else
                                        {
                                            SEH.StringOffset = pattern.IndexOf(combinedSeh);
                                        }
                                        SEH.ThreadID = ThreadsInfo[i].ThreadID;
                                        SEH.RegisterValue = (IntPtr)BitConverter.ToInt64(sehChain.ReturnValue[j].Item1, 0);
                                        registers.Add(SEH);
                                    }
                                    else if (pattern.Contains(SEHValue) || pattern.Contains(ReversedSEHValue) || pattern.Contains(nSEHValue) || pattern.Contains(nReversedSEHValue))
                                    {
                                        SEH.Register = "SEH" + i.ToString();
                                        if (pattern.Contains(ReversedSEHValue))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(ReversedSEHValue);
                                        }
                                        else if (pattern.Contains(SEHValue))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(SEHValue);
                                        }
                                        else if (pattern.Contains(nReversedSEHValue))
                                        {
                                            SEH.StringOffset = pattern.IndexOf(nReversedSEHValue);
                                        }
                                        else
                                        {
                                            SEH.StringOffset = pattern.IndexOf(nSEHValue);
                                        }
                                        SEH.ThreadID = ThreadsInfo[i].ThreadID;
                                        SEH.RegisterValue = (IntPtr)BitConverter.ToInt64(sehChain.ReturnValue[j].Item1, 0);
                                        registers.Add(SEH);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                offsets.Error = new ERCException("Critical Error: Process returned incompatible machine type.");
                offsets.LogEvent();
            }
            offsets.ReturnValue = registers;
            return offsets;
        }
        #endregion

        #endregion

        #region BoyerMoore Search ByteArrays
        /// <summary>
        /// Private function, BoyerMoore string search algorithm modified to search for sets of bytes in a byte array. 
        /// Takes two byte arrays, array to be searched and array to search for.
        /// </summary>
        private static int ByteIndexOf(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0)
            {
                return 0;
            }

            int[] charTable = MakeCharTable(needle);
            int[] offsetTable = MakeOffsetTable(needle);
            for (int i = needle.Length - 1; i < haystack.Length;)
            {
                int j;
                for (j = needle.Length - 1; needle[j] == haystack[i]; --i, --j)
                {
                    if (j == 0)
                    {
                        return i;
                    }
                }

                i += Math.Max(offsetTable[needle.Length - 1 - j], charTable[haystack[i]]);
            }
            return -1;
        }

        private static int[] MakeCharTable(byte[] needle)
        {
            const int ALPHABET_SIZE = 256;
            int[] table = new int[ALPHABET_SIZE];
            for (int i = 0; i < table.Length; ++i)
            {
                table[i] = needle.Length;
            }

            for (int i = 0; i < needle.Length - 1; ++i)
            {
                table[needle[i]] = needle.Length - 1 - i;
            }

            return table;
        }

        private static int[] MakeOffsetTable(byte[] needle)
        {
            int[] table = new int[needle.Length];
            int lastPrefixPosition = needle.Length;
            for (int i = needle.Length - 1; i >= 0; --i)
            {
                if (IsPrefix(needle, i + 1))
                {
                    lastPrefixPosition = i + 1;
                }

                table[needle.Length - 1 - i] = lastPrefixPosition - i + needle.Length - 1;
            }

            for (int i = 0; i < needle.Length - 1; ++i)
            {
                int slen = SuffixLength(needle, i);
                table[slen] = needle.Length - 1 - i + slen;
            }

            return table;
        }

        private static bool IsPrefix(byte[] needle, int p)
        {
            for (int i = p, j = 0; i < needle.Length; ++i, ++j)
            {
                if (needle[i] != needle[j])
                {
                    return false;
                }
            }

            return true;
        }

        private static int SuffixLength(byte[] needle, int p)
        {
            int len = 0;
            for (int i = p, j = needle.Length - 1; i >= 0 && needle[i] == needle[j]; --i, --j)
            {
                len += 1;
            }

            return len;
        }

        #endregion

        #region CreateExcludesList
        /// <summary>
        /// Creates a list of modules to exclude from a search of memory.
        /// </summary>
        /// <returns>Returns an ErcResult containing a list of stringss</returns>
        public List<string> CreateExcludesList(bool aslr = false, bool safeseh = false, bool rebase = false, bool nxcompat = false, bool osdll = false)
        {
            List<string> excludedModules = new List<string>();
            for(int i = 0; i < ModulesInfo.Count; i++)
            {
                bool add = false;
                if(aslr == true && ModulesInfo[i].ModuleASLR == true)
                {
                    add = true;
                }
                if (safeseh == true && ModulesInfo[i].ModuleSafeSEH == true)
                {
                    add = true;
                }
                if (rebase == true && ModulesInfo[i].ModuleRebase == true)
                {
                    add = true;
                }
                if (nxcompat == true && ModulesInfo[i].ModuleNXCompat == true)
                {
                    add = true;
                }
                if (osdll == true && ModulesInfo[i].ModuleOsDll == true)
                {
                    add = true;
                }
                if(add == true)
                {
                    excludedModules.Add(ModulesInfo[i].ModulePath);
                }
            }
            return excludedModules;
        }
        #endregion

        #region Accessors

        #region ToString
        /// <summary>
        /// Override of the ToString method. Returns information about the process in a pleasantly formatted string
        /// </summary>
        /// <returns>A string</returns>
        public override string ToString()
        {
            string ret = "";
            ret += "Process Name = " + ProcessName + Environment.NewLine;
            ret += "Process Description = " + ProcessDescription + Environment.NewLine;
            ret += "Process Path = " + ProcessPath + Environment.NewLine;
            ret += "Process ID = " + ProcessID + Environment.NewLine;
            if(ProcessMachineType == MachineType.I386)
            {
                ret += "Process Handle = 0x" + ProcessHandle.ToString("X8") + Environment.NewLine;
            }
            else
            {
                ret += "Process Handle = 0x" + ProcessHandle.ToString("X16") + Environment.NewLine;
            }
            ret += "Process Machine Type = " + ProcessMachineType.ToString() + Environment.NewLine;

            return ret;
        }
        #endregion

        #region Get Modules Handles
        /// <summary>
        /// Returns a list of module handles associated with the process.
        /// </summary>
        /// <returns>Returns a dictionary containing the handle and path of each module</returns>
        public ErcResult<Dictionary<string, IntPtr>> GetModuleHandles()
        {
            ErcResult<Dictionary<string, IntPtr>> ret = new ErcResult<Dictionary<string, IntPtr>>(ProcessCore);
            if (ProcessModuleHandles.Count > 0)
            {
                ret.ReturnValue = ProcessModuleHandles;
                return ret;
            }
            else
            {
                ret.Error = new ERCException("Error: An unknown eroor has occured whilst populating the modules list for this process. Check the error log for more detailed information.");
                return ret;
            }
        }
        #endregion

        #region Get Module Information
        /// <summary>
        /// Gets the list of ModuleInfo objects associated with the current process.
        /// </summary>
        /// <returns>Returns an ErcResult containing a list of ModuleInfo objects</returns>
        public ErcResult<List<ModuleInfo>> GetProcessModuleInformation()
        {
            ErcResult<List<ModuleInfo>> ret = new ErcResult<List<ModuleInfo>>(ProcessCore);
            if(ModulesInfo.Count > 0)
            {
                ret.ReturnValue = ModulesInfo;
                return ret;
            }
            else
            {
                ret.Error = new ERCException("Error: An unknown eroor has occured whilst populating the modules list for this process. Check the error log for more detailed information.");
                return ret;
            }
        }
        #endregion

        #region Get Thread Information
        /// <summary>
        /// Gets the list of ThreadInfo objects associated with the current process.
        /// </summary>
        /// <returns>Returns an ErcResult containing a list of ThreadInfo objects</returns>
        public ErcResult<List<ThreadInfo>> GetProcessThreadInformation()
        {
            ErcResult<List<ThreadInfo>> ret = new ErcResult<List<ThreadInfo>>(ProcessCore);
            if (ThreadsInfo.Count > 0)
            {
                ret.ReturnValue = ThreadsInfo;
                return ret;
            }
            else
            {
                ret.Error = new ERCException("Error: An unknown eroor has occured whilst populating the threads list for this process. Check the error log for more detailed information.");
                return ret;
            }
        }
        #endregion

        #region Dump Memory Region
        /// <summary>
        /// Reads process memory from a specific address for a set number of bytes. 
        /// </summary>
        /// <param name="startAddress">The address to start reading from.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <returns>Returns a bytes array containing the specified contents of process memory.</returns>
        public ErcResult<byte[]> DumpMemoryRegion(IntPtr startAddress, int length)
        {
            ErcResult<byte[]> result = new ErcResult<byte[]>(ProcessCore);
            byte[] bytes = new byte[length];
            try
            {
                int retValue = ErcCore.ReadProcessMemory(ProcessHandle, startAddress, bytes, length, out int bytesRead);
                if (retValue == 0)
                {
                    ERCException ex = new ERCException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                    result.ReturnValue = bytes;
                    throw ex;
                }
                else
                {
                    result.ReturnValue = bytes;
                }
            }
            catch(Exception e)
            {
                result.Error = e;
            }
            
            return result;
        }
        #endregion

        #endregion
    }
}
