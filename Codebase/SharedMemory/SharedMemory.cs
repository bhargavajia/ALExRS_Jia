using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

/*La classe permette di gestire la comunicazione tra l’interfaccia grafica (GUI) e il robot/hardware usando memoria condivisa. 
 *Consente alla GUI di leggere in tempo reale lo stato del robot (sensori, motori, fault, modalità operative) e di inviargli i comandi (settaggi, movimenti, start/stop, ecc).
 *usando file di memoria condivisa (MemoryMappedFile) per scambiare dati tra processi diversi (ad esempio il programma dell’utente e il controller del robot).
 *Ci sono strutture  e metodi 
 * •le strutture usate per raccogliere i dati sia in ingresso che in uscita (verso la GUI)
 *  quindi sono gruppi ordinati di variabili che memorizzano lo stato corrente del robot o i comandi da inviare.
 *
 * •i metodi permettono di di gestire i dati 
 * 
 */



namespace ALExGUI_4
{
    /// <summary>
    /// The class maps all the shared memories and includes the functions to read robot data and to write the commands for ALEx 
    /// </summary>
    public class SharedMemory
    {
        //----------------------------------------------GUI DATA IN------------------------------------------------------
        /// <summary>
        /// The structure has informations related to the fault codes of a single arm
        /// </summary>
        /// 
                /*    in questa sezione ci sono le strutture per i DATA IN 
                 *    dati dalla macchina verso la GUI
                 */

        [StructLayout(LayoutKind.Sequential)]    
        public struct Fault_Code
        {
            public int Fixed;
            public int Current;
        };
        /// <summary>
        /// The structure has the robot read parameters of a single arm 
        /// posizioni correnti, min/max giunto, offset di spalla, compensazione gravità e fattore bilaterale.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Exos_Arm_Param        
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
	        public float[] Joint_WearingPos;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
	        public float[] Joint_MinPos;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
	        public float[] Joint_MaxPos;
            [MarshalAs(UnmanagedType.R4)]
            public float X_Shulder_Offset;		// metri
            [MarshalAs(UnmanagedType.R4)]
            public float Human_Arm_Gravity;	    // 0 -> 1
            [MarshalAs(UnmanagedType.R4)]
            public float Bilateral_factor;		// 0 -> 1
        };
        /// <summary>
        /// The structure has the read informations realated to the single arm of robot status.
        /// fase, modalità, tool mode) e codici fault dei driver.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Exos_Arm_Status         
        {
            [MarshalAs(UnmanagedType.R4)]
	        public float ControlPhase;
            [MarshalAs(UnmanagedType.R4)]
            public float ControlMode;
            [MarshalAs(UnmanagedType.R4)]       // ALEX32
            public float ToolMode;              // ALEX32

            public Fault_Code FaultCode;
	        public Fault_Code DriverBoard_FaultCode1;
            public Fault_Code DriverBoard_FaultCode2;
	        public Fault_Code Driver_FaultCode1;
            public Fault_Code Driver_FaultCode2;
            public Fault_Code Driver_FaultCode3;
            public Fault_Code Driver_FaultCode4;
        };
        /// <summary>
        /// The structure contains all the informations and the parameters of a single robot arm
        /// struttura che unisce i dati precedenti in un unica struttura
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Exos_Arm //-----------------DUBBIO: non chiaro perchè mette una struct dentro un altra
        {
	        public ALEX30_GUI_DataIn_Exos_Arm_Status Status;
	        public ALEX30_GUI_DataIn_Exos_Arm_Param Param_curr;
        };
        /// <summary>
        /// The structure contains the informations related to low level status
        /// es. temperature CPU 
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Exos_Global_Status   
        {
	        public Fault_Code FaultCode;
            [MarshalAs(UnmanagedType.R4)]
	        public float Rehab_Rec_DataOut;
            [MarshalAs(UnmanagedType.R4)]
	        public float Control_Rec_DataOut;
            [MarshalAs(UnmanagedType.R4)]       // ALEX32
            public float RecPlay_Rec_DataOut;   // ALEX32
            [MarshalAs(UnmanagedType.R4)]       // ALEX32
            public float CPU_Temperature;       // ALEX32
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Exos_Global //----------------DUBBIO: non chiaro perchè mette una struct dentro un altra
        {
	        public ALEX30_GUI_DataIn_Exos_Global_Status Status;
        };        
        /// <summary>
        /// It's a structure that contains all the informations related to robot (booth arms) and low level status
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Exos
        {
	        public ALEX30_GUI_DataIn_Exos_Global Glo;
            public ALEX30_GUI_DataIn_Exos_Arm armRight;
	        public ALEX30_GUI_DataIn_Exos_Arm armLeft;
        };        
        /// <summary>
        /// The structure contains the informations related to the hardware interface connection
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Host_Status
        {
	        public Fault_Code Lib_FaultCode;
//	        public Fault_Code Power_FaultCode;              // ALEX32
            public int Connected;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn_Host
        {
	        public ALEX30_GUI_DataIn_Host_Status Status;
        };

        /// <summary>
        /// The structure contains all the informations related to che robot (booth arms) and the hardware interface
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataIn
        {
	        public ALEX30_GUI_DataIn_Host Host;
	        public ALEX30_GUI_DataIn_Exos Exos;
        };

        //----------------------------------------------GUI DATA OUT------------------------------------------------------
        // contiene le strutture di DATA OUT
        // dati inviati dalla GUI all'exo

        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataOut_Exos_Arm
        {
            [MarshalAs(UnmanagedType.R4)]
	        public float Command;
	        public ALEX30_GUI_DataIn_Exos_Arm_Param Param_des; // invia comandi ad un singolo braccio
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataOut_Exos_Global  // invia comandi validi per entrmbi i bracci
        {
            [MarshalAs(UnmanagedType.R4)]
	        public float Command;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataOut_Exos
        {
	        public ALEX30_GUI_DataOut_Exos_Global Glo;
	        public ALEX30_GUI_DataOut_Exos_Arm armRight;
            public ALEX30_GUI_DataOut_Exos_Arm armLeft;
        };
        
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataOut_Host
        {
            [MarshalAs(UnmanagedType.R4)]
	        public float Command;
        };
        /// <summary>
        /// The structure contains the structures that allows to give a command to booth arms or to the low level software
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_GUI_DataOut
        {
	        public ALEX30_GUI_DataOut_Host Host;
	        public ALEX30_GUI_DataOut_Exos Exos;
        };

        //--------------------------------------------------------REHAB DATA IN-----------------------------------------
        // DATI LETTI DALL'EXO posizioni giunti, velocità, torque, posizione e forza dell’end-effector,
        // pressione handle, e obiettivi desiderati.
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_REHAB_Exos_DataIn
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Pos;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Speed;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Torque;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] EE_Pos;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] EE_Speed;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] EE_Force;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Pos_des_ret;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] EE_Pos_des_ret;
            [MarshalAs(UnmanagedType.R4)]
            public float Handle_Pressure;	
        };
        /// <summary>
        /// The structure contains the specific information related to booth the arm exoskeletons 
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_REHAB_DataIn
        {
            [MarshalAs(UnmanagedType.R4)]
            public float Tmer;
            public ALEX30_REHAB_Exos_DataIn armRight;
            public ALEX30_REHAB_Exos_DataIn armLeft;	
        };

        //------------------------------------------------------------REHAB DATA OUT------------------------------------------------
        // dati inviati uscita per il task riabilitativo.
        [StructLayout(LayoutKind.Sequential)]
        public struct Impedance_str
        {
            [MarshalAs(UnmanagedType.R4)]
            public float K;
            [MarshalAs(UnmanagedType.R4)]
            public float C_rel;
            [MarshalAs(UnmanagedType.R4)]
            public float C_ass;                     // ALEX32
            [MarshalAs(UnmanagedType.R4)]
            public float Speed;                     // ALEX32
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Impedance_base_str
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] K;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] C_rel;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] C_ass;         
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] Speed;       
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Impedance_evo_str
        {
            public Impedance_base_str Pos;
            public Impedance_base_str Neg;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9, ArraySubType = UnmanagedType.R4)]
            public float[] Revo;
        }
        //
        //Consente di scrivere comandi di forza e posizione desiderati per giunti e end-effector;
        //include anche i limiti e impedenze articolari.
        //             ↓↓↓↓

        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_REHAB_Exos_DataOut
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] EE_Force_des;	  
	        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Torque_des;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]
            public float[] EE_Pos_des;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3, ArraySubType = UnmanagedType.R4)]   // ALEX32
            public float[] EE_Vel_des;                                                              // ALEX32
            public Impedance_evo_str EE_Impedance;
	        [MarshalAs(UnmanagedType.R4)]
            public float EE_Speed_max;
            [MarshalAs(UnmanagedType.R4)]
            public float EE_Force_max;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Pos_des;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]       // ALEX32
            public float[] Joint_Vel_des;                                                               // ALEX32
            public Impedance_str Joint_Impedance1;
            public Impedance_str Joint_Impedance2;
            public Impedance_str Joint_Impedance3;
            public Impedance_str Joint_Impedance4;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Speed_max;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.R4)]
            public float[] Joint_Torque_max;                                                            // ALEX32
        };
        /// <summary>
        /// The struct allows to set all the parameters related to booth arm exolskeleton
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ALEX30_REHAB_DataOut
        {
            [MarshalAs(UnmanagedType.R4)]
	        public float Timer;
	        public ALEX30_REHAB_Exos_DataOut armRight;
            public ALEX30_REHAB_Exos_DataOut armLeft;
        };

        //-----------------------------------------------------------------------------------------------
        //                       strutture di comando interfaccia di basso livello
        //                                       ↓ ↓ ↓ ↓ 
        [StructLayout(LayoutKind.Sequential)]
        public struct DataOut_Com_str
        {
            public int Start;
            public int Stop;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Base_Channel_str
        {
            public DataOut_Com_str DataOut1;
            public DataOut_Com_str DataOut2;
            public DataOut_Com_str DataOut3;
            public DataOut_Com_str DataOut4;
            public DataOut_Com_str DataOut5;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5, ArraySubType = UnmanagedType.I4)]
            public int[] WDog_Counter;
        }

        //-----------------------------------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        public struct Play_Com_str
        {
            public int Start;
            public int Pause;
            public int Stop;
            public int Clear;
            public int Load_File;
            public int Load_Record;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Record_Com_str
        {
            public int Start;
            public int Stop;
            public int Clear;
            public int Save;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Config_Com_str
        {
            public int Load;
            public int Save;
            public int Update;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DeviceConfig_Com_str
        {
            public int Load;
            public int Save;
            public int Get;
            public int Set;
        }        
        /// <summary>
        /// The stucture allows to send specific command related to certain functions 
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Base_Command_str
        {
            public Play_Com_str Play;
            public Record_Com_str Record_For_Play;      // ALEX32
            public Record_Com_str Record;
            public Record_Com_str Record_Raw;
            public Record_Com_str Record_Stat;
            public Config_Com_str Config;
            public DeviceConfig_Com_str DeviceConfig;
            int ShutDown;
        }

        //-----------------------------------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        public struct DataOut_Status_str
        {
            public int Is_On;
            public int Is_Blocked_by_WDog;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Play_Status_str
        {
            public int Is_On;
            public int Is_Pause_On;
            public int Is_Loading;
            public int Counter;
            public int Buffer_Index;
            public int Buffer_Total_Length;
            public double Time;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64, ArraySubType = UnmanagedType.R4)]
            public float[] Data_Ini;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64, ArraySubType = UnmanagedType.R4)]
            public float[] Data_Curr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64, ArraySubType = UnmanagedType.R4)]
            public float[] Data_Fin;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Record_Status_str
        {
            public int Is_On;
            public int Is_Saving;
            public int Buffer_Index;
            public int Buffer_Total_Length;
            public int Buffer_IsFull;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Frequency_Status_str
        {
            public int MainThread;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5, ArraySubType = UnmanagedType.I4)]
            public int[] DeviceData;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5, ArraySubType = UnmanagedType.I4)]
            public int[] DeviceData_Old;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5, ArraySubType = UnmanagedType.I4)]
            public int[] CommandData;
            public int RecordData_For_Play;             // ALEX32
            public int RecordData;
            public int RecordData_Raw;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5, ArraySubType = UnmanagedType.I4)]
            public int[] WDog_Ticks;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct WatchDog_Status_str
        {
            public int Counter;
            public int OverTime;
        }

        [StructLayout(LayoutKind.Sequential)]       // ALEX32
        public struct Exec_Dev_Status_srt           // ALEX32
        {
            public int Phase;                       // ALEX32
        }        
        /// <summary>
        /// The structure contains all the status variables of the harware interface
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Base_Status_str
        {
            public Exec_Dev_Status_srt Dev_Status;      // ALEX32
            public DataOut_Status_str DataOut1;
            public DataOut_Status_str DataOut2;
            public DataOut_Status_str DataOut3;
            public DataOut_Status_str DataOut4;
            public DataOut_Status_str DataOut5;
            public Play_Status_str Play;
            public Record_Status_str Record_For_Play;   // ALEX32
            public Record_Status_str Record;
            public Record_Status_str Record_Raw;
            public Record_Status_str Record_Stat;
            public WatchDog_Status_str WDog1;
            public WatchDog_Status_str WDog2;
            public WatchDog_Status_str WDog3;
            public WatchDog_Status_str WDog4;
            public WatchDog_Status_str WDog5;
            public Frequency_Status_str Frequency;
            double Time;
            double DTime;
        };
        /// <summary>
        /// The structure contains current fault status of the harware interface
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct System_Fault_str
        {
            public int fault;
        }
        //                   strutture di comando interfaccia di basso livello.
        //                                  ↑ ↑ ↑ ↑ 
        // 
        //                                    ↓ ↓ ↓ ↓
        //                      MEMORIA CONDIVISA E VARIABILI per la GESTIONE
        MemoryMappedFile memoryFileAppDataIn, memoryFileAppDataOut, memoryFileGuiDataIn, memoryFileGuiDataOut, memoryFileBaseChannel, memoryFileBaseCommand, memoryFileBaseStatus, memoryFileSystemFault;    //classe che rappresenta il file di mappatura della memoria condivisa
        MemoryMappedViewStream streamAccessAppDataOut, streamAccessGuiDataOut, streamAccessAppDataIn, streamAccessGuiDataIn, streamAccessCommand, streamAccessStatus, streamAppOut, streamWriteGuiDataOut, streamChannel, streamAccesFault;  //classe che crea un accesso di tipo sequenziale alla memoria condivisa
        BinaryReader readGuiDataOut, readAppDataIn, readGuiDataIn, readCommand, readStatus, readAppDataOut, readFault;                             //classe che effettua la lettura della memoria 
        BinaryWriter writeGuiDataOut, writeAppOut, writeChannel, writeCommand;
        ALEX30_REHAB_DataIn AppDataInStruct;
        ALEX30_REHAB_DataOut AppDataOutStruct;
        ALEX30_GUI_DataIn GuiDataInStruct;
        ALEX30_GUI_DataOut GuiDataOutStruct;
        Base_Channel_str BaseChannelStruct;
        Base_Command_str BaseCommandStruct;
        Base_Status_str BaseStatusStruct;
        System_Fault_str SystemFaultStruct;                         // ↑ ↑ ↑ 
        const float radToDeg = 57.295779f;                          // ↓ ↓ ↓ definzioni di costanti
        const float degToRad = 0.017453292519943295769f;
        const int ALEX32_COMMAND_EXOS_START_DEVICE = 1;
        const int ALEX32_COMMAND_EXOS_STOP_DEVICE = 11;
        const int ALEX32_COMMAND_EXOS_APPLY_JOINT_LIMIT = 50;
        const int ALEX32_COMMAND_EXOS_APPLY_HUMAN_GRAVITY = 55;
        const int ALEX32_COMMAND_EXOS_STOP_HUMAN_GRAVITY = 56;
        const int ALEX32_COMMAND_EXOS_APPLY_BILATERAL = 65;
        const int ALEX32_COMMAND_EXOS_STOP_BILATERAL = 66;
        const int ALEX32_COMMAND_EXOS_START_REHAB = 3;              // ALEX32
        const int ALEX32_COMMAND_EXOS_STOP_REHAB = 12;
        const int ALEX32_COMMAND_EXOS_CLEARFAULT = 100;             //    ↑ ↑ ↑ 





        public SharedMemory()
        {
            AppDataInStruct = new ALEX30_REHAB_DataIn();
            AppDataOutStruct = new ALEX30_REHAB_DataOut();
            GuiDataInStruct = new ALEX30_GUI_DataIn();
            GuiDataOutStruct = new ALEX30_GUI_DataOut();
            BaseChannelStruct = new Base_Channel_str();
            BaseCommandStruct = new Base_Command_str();
            BaseStatusStruct = new Base_Status_str();
            SystemFaultStruct = new System_Fault_str();

            memoryFileAppDataIn = MemoryMappedFile.OpenExisting("ALEX32_DATA_IN");
            memoryFileAppDataOut = MemoryMappedFile.OpenExisting("ALEX32_DATA_OUT");
            memoryFileGuiDataIn = MemoryMappedFile.OpenExisting("ALEX32_GUI_IN");
            memoryFileGuiDataOut = MemoryMappedFile.OpenExisting("ALEX32_GUI_OUT");
            memoryFileBaseChannel = MemoryMappedFile.OpenExisting("ALEX32_BASE_CHANNEL");
            memoryFileBaseCommand = MemoryMappedFile.OpenExisting("ALEX32_BASE_COMMAND");
            memoryFileBaseStatus = MemoryMappedFile.OpenExisting("ALEX32_BASE_STATUS");
            memoryFileSystemFault = MemoryMappedFile.OpenExisting("SYSTEM_FAULT");

            streamAccessAppDataOut = memoryFileAppDataOut.CreateViewStream();
            streamAccessAppDataIn = memoryFileAppDataIn.CreateViewStream();
            streamAccessGuiDataOut = memoryFileGuiDataOut.CreateViewStream();
            streamAccessGuiDataIn = memoryFileGuiDataIn.CreateViewStream();
            streamChannel = memoryFileBaseChannel.CreateViewStream();
            streamAccessCommand = memoryFileBaseCommand.CreateViewStream();
            streamAccessStatus = memoryFileBaseStatus.CreateViewStream();
            streamWriteGuiDataOut = memoryFileGuiDataOut.CreateViewStream();
            streamAccesFault = memoryFileSystemFault.CreateViewStream();

            readAppDataOut = new BinaryReader(streamAccessAppDataOut);
            readAppDataIn = new BinaryReader(streamAccessAppDataIn);
            readGuiDataOut = new BinaryReader(streamAccessGuiDataOut);            
            readGuiDataIn = new BinaryReader(streamAccessGuiDataIn);
            readCommand = new BinaryReader(streamChannel);        
            readStatus = new BinaryReader(streamAccessStatus);
            readFault = new BinaryReader(streamAccesFault);

            writeGuiDataOut = new BinaryWriter(streamWriteGuiDataOut);
            writeCommand = new BinaryWriter(streamAccessCommand);
            writeChannel = new BinaryWriter(streamChannel);
            writeAppOut = new BinaryWriter(streamAccessAppDataOut);

            JointPosL = new float[8];
            JointPosR = new float[8];
            JointPosDesL = new float[4];
            JointPosDesR = new float[4];
            JointPosMinL = new float[4];
            JointPosMinR = new float[4];
            JointPosMaxL = new float[4];
            JointPosMaxR = new float[4];
            JointPosInitL = new float[4];
            JointPosInitR = new float[4];
            JointTorqueL = new float[4];
            JointTorqueR = new float[4];
            Ee_posL = new float[3];
            Ee_posR = new float[3];
            Ee_forceL = new float[3];
            Ee_forceR = new float[3];
            Ee_speedL = new float[3];
            Ee_speedR = new float[3];
            SelectedArm = 0;        

            byte[] dataCommand = readCommand.ReadBytes(Marshal.SizeOf(typeof(Base_Channel_str)));
            var handleCommand = GCHandle.Alloc(dataCommand, GCHandleType.Pinned);
            BaseChannelStruct = (Base_Channel_str)Marshal.PtrToStructure(handleCommand.AddrOfPinnedObject(), typeof(Base_Channel_str));

            handleCommand.Free();
        }
        //------------------------------ ↓ ↓ ↓ 
        //                         DEFINIZIONE DEI METODI
        /// <summary>
        ///                 QUESTI 3 metodi comandano l'avvio, lo stop e la cancellazione errori 
        /// </summary>
        public void startWearing()
        {
            readGuiDataOutStruct();
            switch (SelectedArm)
            {
                case 1:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_START_DEVICE;
                    break;
                case 2:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_START_DEVICE;
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_START_DEVICE;
                    break;
                case 3:
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_START_DEVICE;
                    break;

            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
        }
        public void clearFault()
        {
            readGuiDataOutStruct();

            GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_CLEARFAULT;
            GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_CLEARFAULT;

            GuiDataOutStruct.Exos.Glo.Command = ALEX32_COMMAND_EXOS_CLEARFAULT;
            GuiDataOutStruct.Host.Command = ALEX32_COMMAND_EXOS_CLEARFAULT;

            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);

            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
            GuiDataOutStruct.Exos.Glo.Command = 0;
            GuiDataOutStruct.Host.Command = 0;
        }
        public void stopWearing()
        {

            readGuiDataOutStruct();
            switch (SelectedArm)
            {
                case 1:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_STOP_DEVICE;
                    break;
                case 2:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_STOP_DEVICE;
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_STOP_DEVICE;
                    break;
                case 3:
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_STOP_DEVICE;
                    break;

            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
        }

        // Metodi imposta i limiti angolari min/max per i giunti sinistri o destri. Convertono gradi→radianti.
        public void applyRangeL(int range1Min, int range2Min, int range3Min, int range4Min, int range1Max, int range2Max, int range3Max, int range4Max)
        {
            readGuiDataOutStruct();
            GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_APPLY_JOINT_LIMIT;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MinPos[0] = range1Min * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MinPos[1] = range2Min * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MinPos[2] = range3Min * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MinPos[3] = range4Min * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MaxPos[0] = range1Max * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MaxPos[1] = range2Max * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MaxPos[2] = range3Max * degToRad;
            GuiDataOutStruct.Exos.armLeft.Param_des.Joint_MaxPos[3] = range4Max * degToRad;
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armLeft.Command = 0;
        }
        public void applyRangeR(int range1Min, int range2Min, int range3Min, int range4Min, int range1Max, int range2Max, int range3Max, int range4Max)
        {
            readGuiDataOutStruct();
            GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_APPLY_JOINT_LIMIT;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MinPos[0] = range1Min * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MinPos[1] = range2Min * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MinPos[2] = range3Min * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MinPos[3] = range4Min * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MaxPos[0] = range1Max * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MaxPos[1] = range2Max * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MaxPos[2] = range3Max * degToRad;
            GuiDataOutStruct.Exos.armRight.Param_des.Joint_MaxPos[3] = range4Max * degToRad;
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
        }

        // metodo per Lettura diretta delle strutture GUI da memoria condivisa,
        public void readGuiDataOutStruct()
        {
            streamAccessGuiDataOut.Position = 0;
            byte[] dataCommand = readGuiDataOut.ReadBytes(Marshal.SizeOf(typeof(ALEX30_GUI_DataOut)));
            var handleCommand = GCHandle.Alloc(dataCommand, GCHandleType.Pinned);
            GuiDataOutStruct = (ALEX30_GUI_DataOut)Marshal.PtrToStructure(handleCommand.AddrOfPinnedObject(), typeof(ALEX30_GUI_DataOut));
            handleCommand.Free();
        }

        public void readGuiDataInStruct()
        {
            streamAccessGuiDataIn.Position = 0;
            byte[] dataCommand = readGuiDataIn.ReadBytes(Marshal.SizeOf(typeof(ALEX30_GUI_DataIn)));
            var handleCommand = GCHandle.Alloc(dataCommand, GCHandleType.Pinned);
            GuiDataInStruct = (ALEX30_GUI_DataIn)Marshal.PtrToStructure(handleCommand.AddrOfPinnedObject(), typeof(ALEX30_GUI_DataIn));
            ArmLeftCompValue = GuiDataInStruct.Exos.armLeft.Param_curr.Human_Arm_Gravity;
            ArmRightCompValue = GuiDataInStruct.Exos.armRight.Param_curr.Human_Arm_Gravity;
            for (int i = 0; i < 4; i++)
            {
                JointPosMinL[i] = GuiDataInStruct.Exos.armLeft.Param_curr.Joint_MinPos[i] * radToDeg;
                JointPosMaxL[i] = GuiDataInStruct.Exos.armLeft.Param_curr.Joint_MaxPos[i] * radToDeg;
                JointPosMinR[i] = GuiDataInStruct.Exos.armRight.Param_curr.Joint_MinPos[i] * radToDeg;
                JointPosMaxR[i] = GuiDataInStruct.Exos.armRight.Param_curr.Joint_MaxPos[i] * radToDeg;
            }
            //DEBUG EXOS COMMENT TO DEBUG

            StatusDeviceL = GuiDataInStruct.Exos.armLeft.Status.ControlPhase;
            StatusDeviceR = GuiDataInStruct.Exos.armRight.Status.ControlPhase;
            Mirror = GuiDataInStruct.Exos.armLeft.Param_curr.Bilateral_factor;

            handleCommand.Free();
        }
        public int readSystemFault()
        {
            streamAccesFault.Position = 0;
            byte[] dataCommand = readFault.ReadBytes(Marshal.SizeOf(typeof(System_Fault_str)));
            var handlefault = GCHandle.Alloc(dataCommand, GCHandleType.Pinned);
            SystemFaultStruct = (System_Fault_str)Marshal.PtrToStructure(handlefault.AddrOfPinnedObject(), typeof(System_Fault_str));
            int falut = SystemFaultStruct.fault;

            handlefault.Free();
            return falut;
        }

        public void readAppDataInStruct()
        {
            streamAccessAppDataIn.Position = 0;
            byte[] dataStatus = readAppDataIn.ReadBytes(Marshal.SizeOf(typeof(ALEX30_REHAB_DataIn)));
            var handleStatus = GCHandle.Alloc(dataStatus, GCHandleType.Pinned);
            AppDataInStruct = (ALEX30_REHAB_DataIn)Marshal.PtrToStructure(handleStatus.AddrOfPinnedObject(), typeof(ALEX30_REHAB_DataIn));
            for (int i = 0; i < 8; i++)
            {
                JointPosL[i] = AppDataInStruct.armLeft.Joint_Pos[i] * radToDeg;
                JointPosR[i] = AppDataInStruct.armRight.Joint_Pos[i] * radToDeg;
            }
            for (int i = 0; i < 4; i++)
            {
                JointTorqueL[i] = AppDataInStruct.armLeft.Joint_Torque[i];
                JointTorqueR[i] = AppDataInStruct.armRight.Joint_Torque[i];
            }
            for (int i = 0; i < 3; i++)
            {
                Ee_posL[i] = AppDataInStruct.armLeft.EE_Pos[i];
                Ee_posR[i] = AppDataInStruct.armRight.EE_Pos[i];
                Ee_forceL[i] = AppDataInStruct.armLeft.EE_Force[i];
                Ee_forceR[i] = AppDataInStruct.armRight.EE_Force[i];
                Ee_speedL[i] = AppDataInStruct.armLeft.EE_Speed[i] * radToDeg;
                Ee_speedR[i] = AppDataInStruct.armRight.EE_Speed[i] * radToDeg;
            }
            handleStatus.Free();
        }

        public void readJointPosDes()
        {
            streamAccessAppDataIn.Position = 0;
            byte[] dataStatus = readAppDataIn.ReadBytes(Marshal.SizeOf(typeof(ALEX30_REHAB_DataIn)));
            var handleStatus = GCHandle.Alloc(dataStatus, GCHandleType.Pinned);
            AppDataInStruct = (ALEX30_REHAB_DataIn)Marshal.PtrToStructure(handleStatus.AddrOfPinnedObject(), typeof(ALEX30_REHAB_DataIn));
            for (int i = 0; i < 4; i++)
            {
                JointPosDesL[i] = AppDataInStruct.armLeft.Joint_Pos_des_ret[i] * radToDeg;
                JointPosDesR[i] = AppDataInStruct.armRight.Joint_Pos_des_ret[i] * radToDeg;
            }            
            handleStatus.Free();
        }
        ///**
        // * legge lo stato del dispositivo
        // */
        public void readStatusDevice()
        {
            streamAccessStatus.Position = 0;
            byte[] dataStatus = readStatus.ReadBytes(Marshal.SizeOf(typeof(Base_Status_str)));
            var handleStatus = GCHandle.Alloc(dataStatus, GCHandleType.Pinned);
            BaseStatusStruct = (Base_Status_str)Marshal.PtrToStructure(handleStatus.AddrOfPinnedObject(), typeof(Base_Status_str));
            RecBufferTotLength = BaseStatusStruct.Record_For_Play.Buffer_Total_Length;      // ALEX32
            IndexRecBuffer = BaseStatusStruct.Record_For_Play.Buffer_Index;             // ALEX32
            IndexPlayBuffer = BaseStatusStruct.Play.Buffer_Index;
            PlayIsOn = BaseStatusStruct.Play.Is_On;
            PlayIsPause = BaseStatusStruct.Play.Is_Pause_On;
            RecordIsOn = BaseStatusStruct.Record_For_Play.Is_On;
            PlayBufferCounter = BaseStatusStruct.Play.Counter;
            for (int i = 0; i < 4; i++)
                JointPosInitR[i] = BaseStatusStruct.Play.Data_Ini[i] * radToDeg;
            for (int i = 4; i < 8; i++)
                JointPosInitL[i - 4] = BaseStatusStruct.Play.Data_Ini[i] * radToDeg;
            
            handleStatus.Free();
        }

        public int Frequency()
        {
            
            return BaseStatusStruct.Frequency.DeviceData[1];
            
        }

        public void watchDogActive()
        {
            BaseChannelStruct.WDog_Counter[0] = 1;
            int size = Marshal.SizeOf(BaseChannelStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseChannelStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeChannel.Seek(0, SeekOrigin.Begin);
            writeChannel.Write(structToByte);
            streamChannel.Seek(0, SeekOrigin.Begin);
            BaseChannelStruct.WDog_Counter[0] = 0;
        }

            public void channelStart()
        {
            BaseChannelStruct.DataOut1.Start = 1;
            int size = Marshal.SizeOf(BaseChannelStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseChannelStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);

            writeChannel.Seek(0, SeekOrigin.Begin);
            writeChannel.Write(structToByte);
            streamChannel.Seek(0, SeekOrigin.Begin);
            BaseChannelStruct.DataOut1.Start = 0;
        }

        //                                      ↓ ↓ ↓
        public void channelStop()
        {
            BaseChannelStruct.DataOut1.Stop = 1;
            int size = Marshal.SizeOf(BaseChannelStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseChannelStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeChannel.Seek(0, SeekOrigin.Begin);
            writeChannel.Write(structToByte);
            streamChannel.Seek(0, SeekOrigin.Begin);
            BaseChannelStruct.DataOut1.Stop = 0;
        }

        public void startRec()
        {
            BaseCommandStruct.Record_For_Play.Start = 1;        // ALEX32
            int size = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseCommandStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByte);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Record_For_Play.Start = 0;        // ALEX32
        }

        public void stopRec()
        {
            BaseCommandStruct.Record_For_Play.Stop = 1;         // ALEX32
            int size = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseCommandStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByte);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Record_For_Play.Stop = 0;         // ALEX32
        }
                                          // comandi per la funzione di registrazione
        public void clearRec()
        {
            BaseCommandStruct.Record_For_Play.Clear = 1;        // ALEX32
            int size = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseCommandStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByte);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Record_For_Play.Clear = 0;        // ALEX32
        }

        public void saveRec()
        {
            BaseCommandStruct.Record_For_Play.Save = 1;         // ALEX32
            int size = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseCommandStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByte);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Record_For_Play.Save = 0;         // ALEX32
        }

        public void stop()
        {
            BaseCommandStruct.Play.Stop = 1;
            int sizeCommand = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByteCommand = new byte[sizeCommand];
            IntPtr ptrCommand = Marshal.AllocHGlobal(sizeCommand);
            Marshal.StructureToPtr(BaseCommandStruct, ptrCommand, true);
            Marshal.Copy(ptrCommand, structToByteCommand, 0, sizeCommand);
            Marshal.FreeHGlobal(ptrCommand);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByteCommand);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Play.Stop = 0;

            AppDataOutStruct.armLeft.Joint_Impedance1.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.Speed = 25.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.Speed = 25.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.Speed = 25.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.Speed = 25.0f;

            AppDataOutStruct.armRight.Joint_Impedance1.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.Speed = 25.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.Speed = 25.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.Speed = 25.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.Speed = 25.0f;

            for (int i = 0; i < 4; i++)
            {
                AppDataOutStruct.armLeft.Joint_Speed_max[i] = 15.0f * degToRad;
                AppDataOutStruct.armRight.Joint_Speed_max[i] = 15.0f * degToRad;
                AppDataOutStruct.armLeft.Joint_Torque_max[i] = 35.0f; //3a
                AppDataOutStruct.armRight.Joint_Torque_max[i] = 35.0f;//3a
            }

            int size = Marshal.SizeOf(AppDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(AppDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeAppOut.Seek(0, SeekOrigin.Begin);
            writeAppOut.Write(structToByte);
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }
        //                                      ↑ ↑ ↑

        public void appParamToInit()
        {
            for (int i = 0; i < 4; i++)
            {
                AppDataOutStruct.armRight.Joint_Pos_des[i] = BaseStatusStruct.Play.Data_Ini[i];
                AppDataOutStruct.armRight.Joint_Vel_des[i] = 0.0f;                                  // ALEX32
                AppDataOutStruct.armRight.Joint_Speed_max[i] = 15.0f * degToRad;
                AppDataOutStruct.armRight.Joint_Torque_max[i] = 35.0f;
            }
            for (int i = 4; i < 8; i++)
            {
                AppDataOutStruct.armLeft.Joint_Pos_des[i - 4] = BaseStatusStruct.Play.Data_Ini[i];
                AppDataOutStruct.armLeft.Joint_Vel_des[i - 4] = 0.0f;                                  // ALEX32
                AppDataOutStruct.armLeft.Joint_Speed_max[i - 4] = 15.0f * degToRad;
                AppDataOutStruct.armLeft.Joint_Torque_max[i - 4] = 35.0f;
            }

            AppDataOutStruct.armRight.Joint_Impedance1.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.Speed = 500.0f;

            AppDataOutStruct.armLeft.Joint_Impedance1.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.Speed = 500.0f;

            int size = Marshal.SizeOf(AppDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(AppDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeAppOut.Seek(0, SeekOrigin.Begin);
            writeAppOut.Write(structToByte);
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }
        public void appImpedence()
        {
            AppDataOutStruct.armRight.Joint_Impedance1.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.Speed = 500.0f;

            AppDataOutStruct.armLeft.Joint_Impedance1.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.Speed = 500.0f;

            int size = Marshal.SizeOf(AppDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(AppDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeAppOut.Seek(0, SeekOrigin.Begin);
            writeAppOut.Write(structToByte);
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }

        public void play()
        {
            BaseCommandStruct.Play.Start = 1;
            int sizeCommand = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByteCommand = new byte[sizeCommand];
            IntPtr ptrCommand = Marshal.AllocHGlobal(sizeCommand);
            Marshal.StructureToPtr(BaseCommandStruct, ptrCommand, true);
            Marshal.Copy(ptrCommand, structToByteCommand, 0, sizeCommand);
            Marshal.FreeHGlobal(ptrCommand);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByteCommand);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Play.Start = 0;

            AppDataOutStruct.armRight.Joint_Impedance1.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.Speed = 500.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.K = 100.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.Speed = 500.0f;

            AppDataOutStruct.armLeft.Joint_Impedance1.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.Speed = 500.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.K = 100.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.Speed = 500.0f;

            for (int i = 0; i < 4; i++)
            {
                AppDataOutStruct.armRight.Joint_Speed_max[i] = 500.0f * degToRad;
                AppDataOutStruct.armRight.Joint_Torque_max[i] = 35.0f;
            }
            for (int i = 0; i < 4; i++)
            {
                AppDataOutStruct.armLeft.Joint_Speed_max[i] = 500.0f * degToRad;
                AppDataOutStruct.armLeft.Joint_Torque_max[i] = 35.0f;
            }

            int size = Marshal.SizeOf(AppDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(AppDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeAppOut.Seek(0, SeekOrigin.Begin);
            writeAppOut.Write(structToByte);
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }
        public void stopSoft()
        {

            AppDataOutStruct.armRight.Joint_Impedance1.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.Speed = 50.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.Speed = 50.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.Speed = 50.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.Speed = 50.0f;

            AppDataOutStruct.armLeft.Joint_Impedance1.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.Speed = 50.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.Speed = 50.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.Speed = 50.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_ass = 10.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.Speed = 50.0f;

            for (int i = 0; i < 4; i++)
            {
                AppDataOutStruct.armRight.Joint_Speed_max[i] = 500.0f * degToRad;
                AppDataOutStruct.armRight.Joint_Torque_max[i] = 35.0f;
            }
            for (int i = 0; i < 4; i++)
            {
                AppDataOutStruct.armLeft.Joint_Speed_max[i] = 500.0f * degToRad;
                AppDataOutStruct.armLeft.Joint_Torque_max[i] = 35.0f;
            }

            int size = Marshal.SizeOf(AppDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(AppDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeAppOut.Seek(0, SeekOrigin.Begin);
            writeAppOut.Write(structToByte);
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);

        }

        public void pause()
        {
            BaseCommandStruct.Play.Pause = 1;
            int sizeCommand = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByteCommand = new byte[sizeCommand];
            IntPtr ptrCommand = Marshal.AllocHGlobal(sizeCommand);
            Marshal.StructureToPtr(BaseCommandStruct, ptrCommand, true);
            Marshal.Copy(ptrCommand, structToByteCommand, 0, sizeCommand);
            Marshal.FreeHGlobal(ptrCommand);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByteCommand);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Play.Pause = 0;
        }

        public void clearPlay()
        {
            BaseCommandStruct.Play.Clear = 1;
            int size = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(BaseCommandStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByte);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Play.Clear = 0;
        }

        public void loadRecordData()
        {
            BaseCommandStruct.Play.Load_Record = 1;
            int sizeCommand = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByteCommand = new byte[sizeCommand];
            IntPtr ptrCommand = Marshal.AllocHGlobal(sizeCommand);
            Marshal.StructureToPtr(BaseCommandStruct, ptrCommand, true);
            Marshal.Copy(ptrCommand, structToByteCommand, 0, sizeCommand);
            Marshal.FreeHGlobal(ptrCommand);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByteCommand);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Play.Load_Record = 0;

        }

        public void loadFileData()
        {
            BaseCommandStruct.Play.Load_File = 1;
            int sizeCommand = Marshal.SizeOf(BaseCommandStruct);
            byte[] structToByteCommand = new byte[sizeCommand];
            IntPtr ptrCommand = Marshal.AllocHGlobal(sizeCommand);
            Marshal.StructureToPtr(BaseCommandStruct, ptrCommand, true);
            Marshal.Copy(ptrCommand, structToByteCommand, 0, sizeCommand);
            Marshal.FreeHGlobal(ptrCommand);
            writeCommand.Seek(0, SeekOrigin.Begin);
            writeCommand.Write(structToByteCommand);
            streamAccessCommand.Seek(0, SeekOrigin.Begin);
            BaseCommandStruct.Play.Load_File = 0;
        }

        public void readAppDataOutStruct()
        {
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);

            byte[] dataStatus = readAppDataOut.ReadBytes(Marshal.SizeOf(typeof(ALEX30_REHAB_DataOut)));
            var handleStatus = GCHandle.Alloc(dataStatus, GCHandleType.Pinned);
            AppDataOutStruct = (ALEX30_REHAB_DataOut)Marshal.PtrToStructure(handleStatus.AddrOfPinnedObject(), typeof(ALEX30_REHAB_DataOut));
            
            handleStatus.Free();
            
        }

        public void startRehab()
        {
            readGuiDataOutStruct();
            switch (SelectedArm)
            {
                case 1:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_START_REHAB;
                    break;
                case 2:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_START_REHAB;
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_START_REHAB;
                    break;
                case 3:
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_START_REHAB;
                    break;

            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);

            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
        }

        public void stopRehab()
        {
            readGuiDataOutStruct();
            switch (SelectedArm)
            {
                case 1:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_STOP_REHAB;
                    break;
                case 2:
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_STOP_REHAB;
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_STOP_REHAB;
                    break;
                case 3:
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_STOP_REHAB;
                    break;

            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);

            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
        }

        // METODI CHE Attivano/disattivano la modalità bilaterale sincronizzando i due bracci.
        public void bilateralFunctionActivation()
        {
            readGuiDataOutStruct();
            if (SelectedArm == 2)
            {
                GuiDataOutStruct.Exos.armLeft.Param_des.Bilateral_factor = 1;
                GuiDataOutStruct.Exos.armRight.Param_des.Bilateral_factor = 1;
                GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_APPLY_BILATERAL;
                GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_APPLY_BILATERAL;
            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }
        public void bilateralFunctionDisactivation()
        {
            readGuiDataOutStruct();
            if (SelectedArm == 2)
            {
                GuiDataOutStruct.Exos.armLeft.Param_des.Bilateral_factor = 0;
                GuiDataOutStruct.Exos.armRight.Param_des.Bilateral_factor = 0;
                GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_STOP_BILATERAL;
                GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_STOP_BILATERAL;
            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }

        //METODI CHE Attivano la compensazione del peso del braccio umano impostando il fattore di gravità.
        public void armWeightCompensationActivation(float armWeightCompensationValue, int arm)
        {
            readGuiDataOutStruct();
            switch (arm)
            {
                case 1:
                    GuiDataOutStruct.Exos.armLeft.Param_des.Human_Arm_Gravity = armWeightCompensationValue;
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_APPLY_HUMAN_GRAVITY;
                    break;                
                case 3:
                    GuiDataOutStruct.Exos.armRight.Param_des.Human_Arm_Gravity = armWeightCompensationValue;
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_APPLY_HUMAN_GRAVITY;
                    break;
            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }
        public void armWeightCompensationDisactivation(float armWeightCompensationValue, int arm)
        {
            readGuiDataOutStruct();
            switch (arm)
            {
                case 1:
                    GuiDataOutStruct.Exos.armLeft.Param_des.Human_Arm_Gravity = armWeightCompensationValue;
                    GuiDataOutStruct.Exos.armLeft.Command = ALEX32_COMMAND_EXOS_STOP_HUMAN_GRAVITY;
                    break;                
                case 3:
                    GuiDataOutStruct.Exos.armRight.Param_des.Human_Arm_Gravity = armWeightCompensationValue;
                    GuiDataOutStruct.Exos.armRight.Command = ALEX32_COMMAND_EXOS_STOP_HUMAN_GRAVITY;
                    break;
            }
            int size = Marshal.SizeOf(GuiDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(GuiDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeGuiDataOut.Seek(0, SeekOrigin.Begin);
            writeGuiDataOut.Write(structToByte);
            streamWriteGuiDataOut.Seek(0, SeekOrigin.Begin);
            GuiDataOutStruct.Exos.armRight.Command = 0;
            GuiDataOutStruct.Exos.armLeft.Command = 0;
            //TEST
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
        }


        //Reinizializza a zero tutti i parametri d’uscita
        //(impedenze, limiti, torques, posizioni). Utile per reset del sistema.
        public void resetDataOut()
        {
           

            for (int j = 0; j < 3; j++)
            {
                AppDataOutStruct.armLeft.EE_Force_des[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Pos.K[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Pos.C_ass[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Pos.C_rel[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Pos.Speed[j] = 1500;
                AppDataOutStruct.armLeft.EE_Impedance.Neg.K[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Neg.C_ass[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Neg.C_rel[j] = 0;
                AppDataOutStruct.armLeft.EE_Impedance.Neg.Speed[j] = 1500;
                AppDataOutStruct.armLeft.EE_Pos_des[j] = 0;
                AppDataOutStruct.armLeft.EE_Vel_des[j] = 0;

                AppDataOutStruct.armRight.EE_Force_des[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Pos.K[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Pos.C_ass[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Pos.C_rel[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Pos.Speed[j] = 1500;
                AppDataOutStruct.armRight.EE_Impedance.Neg.K[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Neg.C_ass[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Neg.C_rel[j] = 0;
                AppDataOutStruct.armRight.EE_Impedance.Neg.Speed[j] = 1500;
                AppDataOutStruct.armRight.EE_Pos_des[j] = 0;
                AppDataOutStruct.armRight.EE_Vel_des[j] = 0;
            }
            
            AppDataOutStruct.armLeft.Joint_Impedance1.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance1.Speed = 10000.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance2.Speed = 10000.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance3.Speed = 10000.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.K = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.C_ass = 0.0f;
            AppDataOutStruct.armLeft.Joint_Impedance4.Speed = 10000.0f;

            AppDataOutStruct.armRight.Joint_Impedance1.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance1.Speed = 10000.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance2.Speed = 10000.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance3.Speed = 10000.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.K = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_rel = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.C_ass = 0.0f;
            AppDataOutStruct.armRight.Joint_Impedance4.Speed = 10000.0f;

            for (int v = 0; v < 4; v++)
            {
                AppDataOutStruct.armLeft.Joint_Pos_des[v] = 0.0f;
                AppDataOutStruct.armLeft.Joint_Vel_des[v] = 0.0f;
                AppDataOutStruct.armLeft.Joint_Speed_max[v] = 0.0f;
                AppDataOutStruct.armLeft.Joint_Torque_max[v] = 0.0f;

                AppDataOutStruct.armRight.Joint_Pos_des[v] = 0.0f;
                AppDataOutStruct.armRight.Joint_Vel_des[v] = 0.0f;
                AppDataOutStruct.armRight.Joint_Speed_max[v] = 0.0f;
                AppDataOutStruct.armRight.Joint_Torque_max[v] = 0.0f;
            }
                       
            for (int p = 0; p < 9; p++)
            {
                AppDataOutStruct.armLeft.EE_Impedance.Revo[p] = 0.0f;
                AppDataOutStruct.armRight.EE_Impedance.Revo[p] = 0.0f;
            }

            AppDataOutStruct.armLeft.EE_Impedance.Revo[0] = 1;
            AppDataOutStruct.armLeft.EE_Impedance.Revo[4] = 1;
            AppDataOutStruct.armLeft.EE_Impedance.Revo[8] = 1;

            AppDataOutStruct.armRight.EE_Impedance.Revo[0] = 1;
            AppDataOutStruct.armRight.EE_Impedance.Revo[4] = 1;
            AppDataOutStruct.armRight.EE_Impedance.Revo[8] = 1;

            AppDataOutStruct.armLeft.EE_Speed_max = 0.2f;
            AppDataOutStruct.armRight.EE_Speed_max = 0.2f;

            AppDataOutStruct.armLeft.EE_Force_max = 15.0f;
            AppDataOutStruct.armRight.EE_Force_max = 15.0f;

            for (int k = 0; k < 4; k++)
            {
                AppDataOutStruct.armLeft.Joint_Torque_des[k] = 0.0f;
                AppDataOutStruct.armRight.Joint_Torque_des[k] = 0.0f;
                
            }
            
            int size = Marshal.SizeOf(AppDataOutStruct);
            byte[] structToByte = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(AppDataOutStruct, ptr, true);
            Marshal.Copy(ptr, structToByte, 0, size);
            Marshal.FreeHGlobal(ptr);
            writeAppOut.Flush();
            
            //writeAppOut = new BinaryWriter(streamAccessAppDataOut);
            writeAppOut.Seek(0, SeekOrigin.Begin);

            writeAppOut.Write(structToByte);
            streamAccessAppDataOut.Seek(0, SeekOrigin.Begin);
            
        }

        public float[] JointPosDesL { get; set; }

        public float[] JointPosDesR { get; set; }

        public float[] JointPosL { get; set; }

        public float[] JointPosR { get; set; }

        public float[] JointPosMaxL { get; set; }

        public float[] JointPosMaxR { get; set; }

        public float[] JointPosMinL { get; set; }

        public float[] JointPosMinR { get; set; }

        public float EnableTurnOff { get; set; }

        public float StatusDeviceL { get; set; }

        public float StatusDeviceR { get; set; }

        public int RecBufferTotLength { get; set; }

        public int IndexRecBuffer { get; set; }

        public int IndexPlayBuffer { get; set; }      

        public int PlayIsOn { get; set; }

        public int PlayIsPause { get; set; }

        public int RecordIsOn { get; set; }

        public int PlayBufferCounter { get; set; }

        public float[] JointPosInitL { get; set; }

        public float[] JointPosInitR { get; set; }

        public float[] JointTorqueL { get; set; }

        public float[] JointTorqueR { get; set; }

        public float[] Ee_speedL { get; set; }

        public float[] Ee_speedR { get; set; }

        public float[] Ee_forceL { get; set; }

        public float[] Ee_forceR { get; set; }

        public float[] Ee_posL { get; set; }

        public float[] Ee_posR { get; set; }

        public float ArmLeftCompValue { get; set; }

        public float ArmRightCompValue { get; set; }

        public int SelectedArm { get; set; }

        public float Mirror { get; set; }

    }
}
