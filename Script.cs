//Merge drive Mananger script v29 by jonathan:

//necessary for MPD-1,MSD-2-21,MSD-2-S,MSD-35
//optional for PDD-1,PDD-3,MID-6-3
//no longer supported: MID-6-2, MPD-2x4, MID-6

//You can add as many of them as want to your ship
//The script finds these drives automatically, even if you build them in survival (no nametagging necessary)

//The Drives are activated automatically by WASD
//Inertial dampening of drives can be toggled with 'z' just like with normal thrusters.

//Run programmable block with argument "toggle" to turn turn everything on or off (you can put this into your toolbar)


//Setup:
//

string mode = "auto";                                  //"auto"=triggered by WASD if toggled on (it is on by default)
                                                                    //"manual"=all forward drives active if toggled on, no dampening  (it is off by default)

double power = 1;                                       //Power of drives in percent when active - can be overclocked beyond 100% (kinda risky)

double dampeningstrength = 0.5;             //Percent value, multiplies with power// 0 turns off inertial dampening completely

double reversepower = 0.5;                      //Mass shift drives can work in reverse mode, this is the power multiplier for that

double gravitycompensation = 5.0;          //Increase if your ship drifts downwards on planets, decrease if your ship drifts upwards

string ignoretag = "[ignore]";                     //Every block with this nametag will be ignored by the script; might come in handy for false positive drive detection

//
//End of setup
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////




//Script body - alter at own risk
//Script body - alter at own risk
//Script body - alter at own risk

//declaring variables
//random stuff
double speedlimit = 0;  //change speedlimit for drives only to debug, leave at 0 for auto detect; set far above real speed limit to disable it
bool idle; MyMovingAverage runtime = new MyMovingAverage(5,5);
Vector3D speedGlobal, speedLocal; double power_tfo, power_tba, power_tri, power_tle, power_tup, power_tdo;
double[] ramp = {0,0,0,0,0,0}; bool autospeedlimit; bool toocomplex=false;

//block lists
List<IMyShipConnector> Connectors; List<IMyExtendedPistonBase> Pistons; List<IMyCargoContainer> Cargos;  //all necessary blocks used in drives
List<IMyAssembler> Assemblers; List<IMyMotorBase> Rotors; List<IMyShipMergeBlock> MergeBlocks;
List<IMyDoor> doors;

//cockpit
List<IMyCockpit> Controllers; public IMyCockpit ShipReference;  //needed to figure out orientation

//drives
List<MergeDrive> MergeDrives; List<PistonDrive> PistonDrives;  //classes for the drives itself, MID has no class, just group
List<MassDrive> MassDrives; List<MergeDampener> MergeDampeners;

//controllers
PID_Controller myControllerX, myControllerY, myControllerZ;     //controllers in 3 axis for inertial dampening


//The manager programm, creates a class for each drive
//All following methods are only run once after recompile (or at auto reinitialize every second)
public Program() 
{
    Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100; //updates script every tick and additionally every 100 ticks to reinitialize
    
    MergeDrives = new List<MergeDrive>();   //creates lists for all drive types, not in init as to not delete them all the time
    MassDrives = new List<MassDrive>();
    PistonDrives = new List<PistonDrive>();
    MergeDampeners = new List<MergeDampener>();

    if(mode=="auto") idle=false;    //turns drive on by default in auto mode
    else idle=true;     //turns drive off by default in manual mode
    
    if(speedlimit==0) {autospeedlimit=true; speedlimit=100; }   //determines if speedlimit should be set on auto or is manual
    else {autospeedlimit=false;}
    
    Init();     //Initialize drives and sort blocks into classes
}

void Init()
{
    FindController();   //Searches for a Cockpit etc. to use as orientation reference
    
    toocomplex = false;
    
    speedGlobal = new Vector3D(0,0,0);
    speedLocal = new Vector3D(0,0,0);
    
    //creates 3 new PID_controllers for inertial dampening, values are gain for proportional, integral and differential
    myControllerX = new PID_Controller(0.04,0.2,0.1);   
    myControllerY = new PID_Controller(0.04,0.2,0.1);
    myControllerZ = new PID_Controller(0.04,0.2,0.1);
    
    //pile up all necessary blocks, they could all be part of a drive, or not
    MergeBlocks = new List<IMyShipMergeBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyShipMergeBlock>(MergeBlocks);
    
    Rotors = new List<IMyMotorBase>();
    GridTerminalSystem.GetBlocksOfType<IMyMotorBase>(Rotors);
    
    Pistons = new List<IMyExtendedPistonBase>();
    GridTerminalSystem.GetBlocksOfType<IMyExtendedPistonBase>(Pistons);
    
    Cargos = new List<IMyCargoContainer>();
    GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(Cargos);
    
    Assemblers = new List<IMyAssembler>();
    GridTerminalSystem.GetBlocksOfType<IMyAssembler>(Assemblers);
    
    Connectors = new List<IMyShipConnector>();
    GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(Connectors);
    
    doors = new List<IMyDoor>();
    GridTerminalSystem.GetBlocksOfType<IMyDoor>(doors);
    
    //remove unnecessary blocks, improves performance
    for(int i = Cargos.Count-1; i>-1; i--)
    {
        if(Cargos[i].BlockDefinition.SubtypeId.ToString() != "LargeBlockSmallContainer" && Cargos[i].BlockDefinition.SubtypeId.ToString() != "SmallBlockMediumContainer") 
        {
            Cargos.RemoveAt(i); //remove lockers and shit
            continue;
        }
        if(Cargos[i].CustomName.Contains(ignoretag)) Cargos.RemoveAt(i);
    }
        
    for(int i = Assemblers.Count-1; i>-1; i--)
    {
        if(Assemblers[i].BlockDefinition.SubtypeId.ToString() != "BasicAssembler") 
        {
            Assemblers.RemoveAt(i); //remove large assemblers
            continue;
        }
        if(Assemblers[i].CustomName.Contains(ignoretag)) Assemblers.RemoveAt(i);
    }
        
    for(int i = Rotors.Count-1; i>-1; i--)
    {
        if(Rotors[i].BlockDefinition.TypeId.ToString() == "MyObjectBuilder_MotorSuspension") 
        {
            Rotors.RemoveAt(i); //remove wheels
            continue;
        }
        if(Rotors[i].CustomName.Contains(ignoretag)) Rotors.RemoveAt(i);
    }
        
    for(int i = doors.Count-1; i>-1; i--)
    {
        if(doors[i].BlockDefinition.SubtypeId.ToString() == "LargeBlockGate" || doors[i].BlockDefinition.SubtypeId.ToString() == "LargeBlockOffsetDoor") 
        {
            doors.RemoveAt(i); //remove not useful doors
            continue;
        }
        if(doors[i].CustomName.Contains(ignoretag)) doors.RemoveAt(i);
    }
    
    for(int i = Pistons.Count-1; i>-1; i--)
        if(Pistons[i].CustomName.Contains(ignoretag)) Pistons.RemoveAt(i);
    for(int i = Connectors.Count-1; i>-1; i--)
        if(Connectors[i].CustomName.Contains(ignoretag)) Connectors.RemoveAt(i);
    for(int i = MergeBlocks.Count-1; i>-1; i--)
        if(MergeBlocks[i].CustomName.Contains(ignoretag)) MergeBlocks.RemoveAt(i);
    
    
    //find new drives, doesn't delete old ones
    InitDampeners();
    InitPistonDrives();
    InitMergeDrives();
    InitMassDrives();
    
    //Makes the drives figure out their orientation and save it
    for(int i = 0; i < MergeDrives.Count; i++) MergeDrives[i].FigureOrientation(ShipReference);   
    for(int i = 0; i < PistonDrives.Count; i++) PistonDrives[i].FigureOrientation(ShipReference);
    for(int i = 0; i < MassDrives.Count; i++) MassDrives[i].FigureOrientation(ShipReference);
    for(int i = 0; i < MergeDampeners.Count; i++) MergeDampeners[i].FigureOrientation(ShipReference);
    
    //Echo("Instruction Count: " + Runtime.CurrentInstructionCount.ToString());
    //Echo(MergeDrives[11111111].MergeBlocks[0].CustomName.ToString());     //debug crash after init
}

void FindController()
{
    Controllers = new List<IMyCockpit>();
    GridTerminalSystem.GetBlocksOfType<IMyCockpit>(Controllers);
    
    for(int i=Controllers.Count()-1; i>-1; i--) if(Controllers[i].CanControlShip == false) Controllers.RemoveAt(i); //remove bathrooms, couches etc.
    
    for(int i=0; i<Controllers.Count();i++)
        if (Controllers[i].IsMainCockpit && Controllers[i].CubeGrid==Me.CubeGrid) {ShipReference = Controllers[i]; return;}     //first checks if there is any main cockpit
    
    for(int i=0; i<Controllers.Count();i++)
        if (Controllers[i].IsUnderControl && Controllers[i].CubeGrid==Me.CubeGrid) {ShipReference = Controllers[i]; return;}    //if not, checks if there is any controlled cockpit
    
	for(int i=0; i<Controllers.Count();i++)
		if( Controllers[i].CubeGrid==Me.CubeGrid) ShipReference = Controllers[i];   //just uses the first one it finds, if it is on the main grid
	
    if(!Controllers.Any()) { Echo("No Cockpit found!"); Echo(""); ShipReference = null; }
}

void InitMergeDrives()
{
    for(int i=0; i < MergeBlocks.Count(); i++)  //iterate through the merge blocks, each one can be the basis of a drive
    {
        if(Runtime.CurrentInstructionCount > 40000) 
        {
            toocomplex=true;    //fail safe to prevent crashing, will then maybe fail to detect all drives
            break;
        }
        
        List<IMyMotorBase> RotorsTemp = new List<IMyMotorBase>();   //rotor stack on subgrid
        IMyMotorBase MainRotor = null;      //rotor on main grid
        List<IMyShipMergeBlock> MergeTemp = new List<IMyShipMergeBlock>();  //merge stack on subgrid
        //IMyShipMergeBlock MainMerge = null;       //merge on main grid, not used by script, always on
        IMyShipConnector ConnectorTemp = null;  //connector on subgrid
        IMyShipConnector MainConnector = null;  //connector on main grid
        
        MergeTemp.Add(MergeBlocks[i]);
        if(MergeBlocks[i].CubeGrid==ShipReference.CubeGrid) continue;   //check if on main grid, then abort
        
        bool alreadyexists=false;
        for(int z=0; z<MergeDrives.Count;z++)
            for(int a=0; a<MergeDrives[z].MergeBlocks.Count();a++) 
                if(MergeDrives[z].MergeBlocks[a].EntityId==MergeTemp[0].EntityId) alreadyexists=true;   //check if already exists in any drive
            
        if(alreadyexists==true) continue;
        
        for(int y = 0; y < MergeBlocks.Count(); y++)  // check for more than one merge block
        {
            if(MergeBlocks[y].CubeGrid==MergeBlocks[i].CubeGrid && y!=i)  //check if merge blocks are on top of top rotor
            {                           
                MergeTemp.Add(MergeBlocks[y]);  //assemble merge stack
            }
        }
        
        for(int j = 0; j < Rotors.Count(); j++)
        {
            if(Rotors[j].TopGrid==MergeBlocks[i].CubeGrid)  //check if merge block is on top of any rotor
            {
                int tempindex = j;
                bool done = false;
                RotorsTemp.Add(Rotors[j]);
                
                while(done==false)  //do this until no more rotors are in line, usually just once (until one more is found)
                {
                    int tempcount=RotorsTemp.Count();   //A -> temp save number of rotors ->B
                    for(int k=0; k<Rotors.Count(); k++)     //search for rotors until one on the main sub grid is found
                    {
                        if(Rotors[k].TopGrid==Rotors[tempindex].CubeGrid)   //if rotor is below previous found rotor
                        {
                            RotorsTemp.Add(Rotors[k]);
                            tempindex=k;
                            break;
                        }
                    }
                    for(int m = 0; m < Connectors.Count; m++)   //checks if rotor is on same grid as connector
                    {
                        if(Rotors[tempindex].CubeGrid==Connectors[m].CubeGrid && Connectors[m].CubeGrid!=ShipReference.CubeGrid && Connectors[m].Status==MyShipConnectorStatus.Connected)
                        {
                            ConnectorTemp=Connectors[m];
                            MainConnector=Connectors[m].OtherConnector;
                            done=true;
                            break;
                        }
                    }
                    if(RotorsTemp.Count()==tempcount) break;    //-> B number of rotors in list didn't change ->abort
                }
                
                for(int k=0; k<Rotors.Count(); k++)     //search for main grid rotor
                {
                    if(Rotors[k].TopGrid==Rotors[tempindex].CubeGrid && Rotors[k].CubeGrid==ShipReference.CubeGrid)   //if rotor is below previous found rotor
                    {
                        MainRotor = Rotors[k];
                        tempindex=k;
                        break;
                    }
                }
                
                if(ConnectorTemp!=null && MainConnector!=null && RotorsTemp.Any() && MergeTemp.Any())
                {
                    MergeDrives.Add(new MergeDrive(RotorsTemp,MergeTemp,MainConnector,ConnectorTemp, MainRotor));     //give drive its corresponding blocks
                    break;  //found fitting drive, no need to iterate through the rest
                }
            }
        }
    }
}

void InitPistonDrives()
{
    for (int i = 0; i < Pistons.Count(); i++)   //each piston could be a drive
    {
        if(Runtime.CurrentInstructionCount > 40000) 
        {
            toocomplex=true;    //fail safe to prevent crashing, will then maybe fail to detect all drives
            break;
        }
        
        bool alreadyexists=false;
        for(int z=0; z<PistonDrives.Count;z++) if(PistonDrives[z].Piston.EntityId==Pistons[i].EntityId) alreadyexists=true;  //checks if drive already exists
        if(alreadyexists==true) continue;
        
        if(Pistons[i].CubeGrid!=ShipReference.CubeGrid) continue;   //checks if piston is on main grid
        
        Vector3I Distance = new Vector3I(0,0,0);
        switch(Pistons[i].Orientation.Up)   //creates vector to add to position in order to find door
        {
            case Base6Directions.Direction.Forward: //pointing backward
                Distance.Z=-1;
                break;
            case Base6Directions.Direction.Backward:    //pointing forward
                Distance.Z=1;
                break;
            case Base6Directions.Direction.Up:  //pointing down
                Distance.Y=1;
                break;
            case Base6Directions.Direction.Down:    //pointing up
                Distance.Y=-1;
                break;
            case Base6Directions.Direction.Left:    //pointing right
                Distance.X=-1;
                break;
            case Base6Directions.Direction.Right:   //pointing left
                Distance.X=1;
                break;
        }
        
        for (int k = 0; k< doors.Count(); k++)  //search all doors for fitting one
        {
            if(doors[k].Position==Pistons[i].Position+(Distance*4)) { PistonDrives.Add(new PistonDrive(Pistons[i], doors[k], false)); break;} //pdd with normal door and pillar
            else if(doors[k].Position==Pistons[i].Position+(Distance*5)) { PistonDrives.Add(new PistonDrive(Pistons[i], doors[k], true)); break;}   //pdd with hangar door
        }
    }
}

void InitMassDrives()
{
    //makes list of assemblers and cargo containers, both could be used as a base block
    List<IMyTerminalBlock> AssAndCargo = new List<IMyTerminalBlock>(Assemblers.Count + Cargos.Count);
    AssAndCargo.AddRange(Cargos);
    AssAndCargo.AddRange(Assemblers);
    
    for(int i =0; i<AssAndCargo.Count(); i++)
    {
        if(Runtime.CurrentInstructionCount > 40000) 
        {
            toocomplex=true;    //fail safe to prevent crashing, will then maybe fail to detect all drives
            break;
        }
        
        if(AssAndCargo[i].CubeGrid!=ShipReference.CubeGrid) continue;   //not on main grid, try next
        
        bool alreadyexists=false;
        for(int z=0; z<MassDrives.Count;z++) if(MassDrives[z].BaseBlock.EntityId==AssAndCargo[i].EntityId) alreadyexists=true;  //checks if drive already exists
        if(alreadyexists==true) continue;
        
        for(int m=0; m<Cargos.Count(); m++) //search for top block
        {
            if(Cargos[m].CubeGrid==ShipReference.CubeGrid) continue;    //cargo on main grid, next one
            if(Cargos[m].EntityId == AssAndCargo[i].EntityId) continue; //same block as base, go on with next block
            if(Cargos[m].GetInventory().IsConnectedTo(AssAndCargo[i].GetInventory()))    //found fitting inventories, now searching corresponding rotors
            {
                for(int k=0; k<Rotors.Count(); k++) //first rotor
                {
                    if(Rotors[k].TopGrid==Cargos[m].CubeGrid)   //found top rotor
                    {
                        int tempindex = k;
                        bool done = false;
                        List<IMyMotorBase> RotorsTemp = new List<IMyMotorBase>();
                        RotorsTemp.Add(Rotors[k]);
                        
                        while(done==false)  //do this until no more rotors are in line, usually just once (until one more is found)
                        {
                            int tempcount=RotorsTemp.Count();   //A -> temp save number of rotors ->B
                            for(int j=0; j<Rotors.Count(); j++)     //search for more rotors until one on the main grid is found
                            {
                                if(Rotors[j].TopGrid==Rotors[tempindex].CubeGrid)
                                {
                                    RotorsTemp.Add(Rotors[j]);
                                    tempindex=j;
                                    if(Rotors[j].CubeGrid==ShipReference.CubeGrid) { done=true; break; }  //break if rotor is on main grid
                                }
                            }
                            if(RotorsTemp.Count()==tempcount) break;    //-> B number of rotors in list didn't change ->abort
                        }
                        RotorsTemp.Reverse(); //so that the bottom rotor is first in the list, useful for finding orientation
                        
                        MassDrives.Add(new MassDrive(RotorsTemp, AssAndCargo[i], Cargos[m], reversepower));
                        break;
                    }
                }
                break;
            }
        }
    }
}

void InitDampeners()
{
    for(int i=0; i < MergeBlocks.Count(); i++)  //iterate through the merge blocks, each one can be the basis of a drive
    {
        if(Runtime.CurrentInstructionCount > 40000) 
        {
            toocomplex=true;    //fail safe to prevent crashing, will then maybe fail to detect all drives
            break;
        }
        if(MergeBlocks[i].CubeGrid==ShipReference.CubeGrid) continue;   //check if on main grid, then abort
        
        List<IMyMotorBase> RotorsTemp = new List<IMyMotorBase>();   //rotor stack, just checks for a single one of both
        List<IMyShipMergeBlock> MergeTemp = new List<IMyShipMergeBlock>();  //merge stack on subgrid
        
        MergeTemp.Add(MergeBlocks[i]);
        
        bool alreadyexists=false;
        for(int z=0; z<MergeDampeners.Count;z++)
        {
            for(int a=0; a<MergeDampeners[z].MergeBlocks.Count();a++) 
            {
                if(MergeDampeners[z].MergeBlocks[a].EntityId==MergeTemp[0].EntityId) alreadyexists=true;   //check if already exists in any drive
            }
        }
        if(alreadyexists==true) continue;
        
        for(int y = 0; y < MergeBlocks.Count(); y++)  // check for more than one merge block
        {
            if(MergeBlocks[y].CubeGrid==MergeBlocks[i].CubeGrid && y!=i)
            {                           
                MergeTemp.Add(MergeBlocks[y]);  //assemble merge stack
            }
        }
        
        for(int k=0; k<Rotors.Count();k++)  //assemble rotor stack
        {
            if(Rotors[k].TopGrid==MergeTemp[0].CubeGrid)
            {
                for(int j=0; j<Rotors.Count(); j++)     //search for last rotor
                {
                    if(Rotors[j].TopGrid==Rotors[k].CubeGrid && Rotors[j].CubeGrid==ShipReference.CubeGrid)   //if rotor is below previous found rotor and on main grid
                    {
                        RotorsTemp.Add(Rotors[j]);
                        RotorsTemp.Add(Rotors[k]);
                        break;
                    }
                }
                break;
            }
        }
        
        if(RotorsTemp.Count()==2 && MergeTemp.Any())
        {
            MergeDampeners.Add(new MergeDampener(MergeTemp, RotorsTemp));     //give drive its corresponding blocks
            break;  //found fitting drive, no need to iterate through the rest
        }
    }
}


//All methods from here on are run every tick
void Main(string args, UpdateType updateSource)
{   
    if ((updateSource & UpdateType.Update100) != 0) 
    {
        Init(); //Additional Initialization every 100 ticks to add new drives, doesn't reinitialize the drives. Also checks for controller again
    }
    
    ErrorHandler();     //shows you what you fucked up this time
    
    RemoveDamagedDrives();  //checks if any drives are damaged and remove them from list, also relocks connectors and rotors
    
    for(int i=0; i<PistonDrives.Count; i++)
        if(PistonDrives[i].hangar==true && PistonDrives[i].door.OpenRatio<=0.5) PistonDrives[i].door.Enabled=false; //stops hangar doors at halfway open
    
    //calc speed of ship, used for inertial dampening and power controlling
    speedGlobal = ShipReference.GetShipVelocities().LinearVelocity;
    speedGlobal += gravitycompensation*ShipReference.GetNaturalGravity()/60;    //add fake speed depending on natural gravity, helps prevent downward drift on planets, not very elegant, but works
    speedLocal = Vector3D.TransformNormal(speedGlobal, MatrixD.Transpose(ShipReference.WorldMatrix));

    if(autospeedlimit==true) 
        if(ShipReference.GetShipSpeed()>speedlimit) speedlimit=ShipReference.GetShipSpeed();    //calculates speedlimit automatically, increases until ceiling

    SmartPower(); //calculates power automatically in all directions
    
    Argumenthandler(args);  //this looks at the current idle state, toggles it if args is toggle and fires the drives with correct logic
    
    EchoFunction();     //Simply Echoes a bunch of stuff
}

void ErrorHandler()
{
    if(toocomplex==true) Echo("Too many drives, some drives not detected!");
    if(mode!="auto" && mode!="manual") Echo("Incorrect mode!");
    if(power<0) Echo("Incorrect power!");
    if(power>1) Echo("Overclocked power!");
    if(reversepower<0) Echo("Incorrect reversepower!");
    if(reversepower<0) Echo("Overclocked reversepower!");
    if(dampeningstrength<0) Echo("Incorrect dampeningstrength!");
    if(dampeningstrength>1) Echo("Overclocked dampeningstrength!");
    if(speedlimit<0) Echo("Incorrect speedlimit!");
}

void EchoFunction()
{   
    Echo("Instruction Count: " + Runtime.CurrentInstructionCount.ToString());  //debug, shows current instructions
    runtime.Enqueue((float) Runtime.LastRunTimeMs);
    Echo("Runtime: " + string.Format("{0:0.000}", runtime.Avg) );
    Echo("Speedlimit: " + string.Format("{0:0.0}", speedlimit));
    Echo("ShipController: " + ShipReference.CustomName);
    Echo("Dampeners found: " + (MergeDampeners.Count()).ToString());
    Echo("Merge Drives found: " + MergeDrives.Count().ToString());
    Echo("Piston Drives found: " + PistonDrives.Count().ToString());
    Echo("Mass Shifting Drives found: " + MassDrives.Count().ToString());
    Echo("");
    for(int i=0; i<MergeDrives.Count; i++) Echo("Merge Drive " + (i+1).ToString() + ": " + MergeDrives[i].orientation);
    for(int i=0; i<PistonDrives.Count; i++) Echo("Piston Drive " + (i+1).ToString() + ": " + PistonDrives[i].orientation);
    for(int i=0; i<MassDrives.Count; i++) Echo("Mass Drive " + (i+1).ToString() + ": " + MassDrives[i].orientation);
    for(int i=0; i<MergeDampeners.Count; i++) Echo("Dampener " + (i+1).ToString() + ": " + MergeDampeners[i].orientation);
}

void SmartPower()
{
    //Power drop off between 85% and 95% of speedlimit from 1 to 0
    //Power_t is clamped to power and 0
    power_tle=(9.5-10*(speedLocal.X/speedlimit))*power; 
    if(power_tle>power) power_tle=power; else if(power_tle<0) power_tle=0;
    
    power_tri=(9.5-10*(-speedLocal.X/speedlimit))*power; 
    if(power_tri>power) power_tri=power; else if(power_tri<0) power_tri=0;
    
    power_tup=(9.5-10*(speedLocal.Y/speedlimit))*power; 
    if(power_tup>power) power_tup=power; else if(power_tup<0) power_tup=0;
    
    power_tdo=(9.5-10*(-speedLocal.Y/speedlimit))*power; 
    if(power_tdo>power) power_tdo=power; else if(power_tdo<0) power_tdo=0;
    
    power_tba=(9.5-10*(speedLocal.Z/speedlimit))*power; 
    if(power_tba>power) power_tba=power; else if(power_tba<0) power_tba=0;
    
    power_tfo=(9.5-10*(-speedLocal.Z/speedlimit))*power; 
    if(power_tfo>power) power_tfo=power; else if(power_tfo<0) power_tfo=0;
    
    
    //power ramp up after inactive, takes 10 ticks from 20% to 100% power, multiplies with power_t
    string[] orient = {"left","right","down","up","forward","backward"};
    string[] orientreverse = {"right","left","up","down","backward","forward"};
    
    for(int i=0; i<6;i++)
    {
        //slowly ramps up thrust to prevent high jerk
        if(ramp[i]<1)
        {
            ramp[i]=ramp[i]+0.08;
        }
        
        //check if any active drives in this direction
        bool active = false;
        
        for(int k = 0; k < MergeDrives.Count; k++) 
            if(MergeDrives[k].orientation==orient[i])
                if(MergeDrives[k].active==true) active=true;
            
        for(int k = 0; k < MassDrives.Count; k++) 
        {
            if(MassDrives[k].orientation==orient[i])
                if(MassDrives[k].active==true && MassDrives[k].reverse==false) active=true;
            if(MassDrives[k].orientation==orientreverse[i])
                if(MassDrives[k].active==true && MassDrives[k].reverse==true) active=true;
        }
        
        for(int k = 0; k < PistonDrives.Count; k++) 
            if(PistonDrives[k].orientation==orient[i])
                if(PistonDrives[k].active==true) active=true;
        
        if(active==false) ramp[i]=0.2;  //reset ramp to 20% if all drives in direction are inactive
    }
    power_tle *= ramp[0]; power_tri *= ramp[1]; power_tdo *= ramp[2]; power_tup *= ramp[3]; power_tfo *= ramp[4]; power_tba *= ramp[5];
}

void RemoveDamagedDrives()
{
    for(int i=MergeDrives.Count - 1; i > -1; i--)    //iterates backwards to prevent skipping of index
    {
        if(MergeDrives[i].CheckDestruction()==true) MergeDrives.RemoveAt(i);  //drives respond with true if they are damaged
    }
    
    for(int i=PistonDrives.Count - 1; i > -1; i--)   
    {
        if(PistonDrives[i].CheckDestruction()==true) PistonDrives.RemoveAt(i);  //drives respond with true if they are damaged
    }
    
    for(int i=MassDrives.Count - 1; i > -1; i--)     
    {
        if(MassDrives[i].CheckDestruction()==true) MassDrives.RemoveAt(i);  //drives respond with true if they are damaged
    }
    
    for(int i=MergeDampeners.Count - 1; i> -1; i--)
    {
        if(MergeDampeners[i].CheckDestruction()==true) MergeDampeners.RemoveAt(i);  //drives respond with true if they are damaged
    }
}

void Argumenthandler(string args)
{
    //this allows you to check after a run which drives were activated, used for smart power and shutting off unused drives
    for(int i = 0; i < MergeDrives.Count; i++) MergeDrives[i].active=false;
    for(int i = 0; i < MassDrives.Count; i++) MassDrives[i].active=false;
    for(int i = 0; i < PistonDrives.Count; i++) PistonDrives[i].active=false;
    
    if(idle==true)    //argument handler in default state
    {
        Echo("Idling");
        for(int i=0; i<MergeDrives.Count;i++) 
            for(int k=0; k<MergeDrives[i].MergeBlocks.Count;k++) MergeDrives[i].MergeBlocks[k].Enabled=false;    //turn off merge blocks of drive when drive is off
        for(int i=0; i<MergeDampeners.Count;i++) 
            for(int k=0; k<MergeDampeners[i].MergeBlocks.Count;k++)MergeDampeners[i].MergeBlocks[k].Enabled=false;       //turn off merge blocks of MIDs when drive is off
        for(int i=0; i<PistonDrives.Count;i++) PistonDrives[i].StopDrive();             //stops piston drives, pulls out piston
        //no need to stop mass drive, it stops itself
       
        if(args=="toggle") idle=false;  //switch state
    }
    else           //argument handler while drive is running
    {
        Echo("Active");
        if(args=="toggle") idle=true;   //switch state
        
        
        //this passage handles the MID Inertial Dampeners
        
        for(int j=0; j<MergeDampeners.Count();j++)  //iterate through all merge dampeners, usually just one
        {
            if(MergeDampeners[j].orientation=="wrong direction")    //skip if oriented wrong way
            {
                for(int i = 0; i <MergeDampeners[j].MergeBlocks.Count ; i++) MergeDampeners[j].MergeBlocks[i].Enabled=false;
                continue;
            }
            int index = (int) ((-(speedLocal.Z)/(speedlimit*0.75))*MergeDampeners[j].MergeBlocks.Count() );   //index determines how many merge blocks will be turned on: all on above 75% of speedlimit
            if (index>MergeDampeners[j].MergeBlocks.Count()) index = MergeDampeners[j].MergeBlocks.Count();
            if (index<0) index=0;
            
            if(dampeningstrength==0) index=0;   //turns off mids if dampening is turned off
                
            for(int i = 0; i < index; i++)
                MergeDampeners[j].MergeBlocks[i].Enabled=true;
            
            for(int i = index; i <MergeDampeners[j].MergeBlocks.Count ; i++)
                MergeDampeners[j].MergeBlocks[i].Enabled=false;
        }
        //from here on only real drives
        
        
        if(mode=="auto")    //drive handled by wasd
        {
            if(ShipReference.MoveIndicator.X==1)    //WASD Logic, fires drives corresponding to keyboard input, might not work with gamepad
            {
                foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="right") drive.FireDrive(power_tri);
                foreach(MassDrive drive in MassDrives)
                    if(drive.orientation=="right") drive.FireDrive(power_tri,false);
                foreach(MassDrive drive in MassDrives) 
                    if(drive.orientation=="left") drive.FireDrive(power_tri,true);
                foreach(MergeDrive drive in MergeDrives) 
                    if(drive.orientation=="right") drive.FireDrive(power_tri);
            }
            if(ShipReference.MoveIndicator.X==-1) 
            {
                foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="left") drive.FireDrive(power_tle);
                foreach(MassDrive drive in MassDrives)
                    if(drive.orientation=="left") drive.FireDrive(power_tle,false);
                foreach(MassDrive drive in MassDrives) 
                    if(drive.orientation=="right") drive.FireDrive(power_tle,true);
                foreach(MergeDrive drive in MergeDrives) 
                    if(drive.orientation=="left") drive.FireDrive(power_tle);
            }
            
            if(ShipReference.MoveIndicator.Y==1)    
            {
                foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="up") drive.FireDrive(power_tup);
                foreach(MassDrive drive in MassDrives)
                    if(drive.orientation=="up") drive.FireDrive(power_tup,false);
                foreach(MassDrive drive in MassDrives) 
                    if(drive.orientation=="down") drive.FireDrive(power_tup,true);
                foreach(MergeDrive drive in MergeDrives) 
                    if(drive.orientation=="up") drive.FireDrive(power_tup);
            }
            if(ShipReference.MoveIndicator.Y==-1) 
            {
                foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="down") drive.FireDrive(power_tdo);
                foreach(MassDrive drive in MassDrives)
                    if(drive.orientation=="down") drive.FireDrive(power_tdo,false);
                foreach(MassDrive drive in MassDrives) 
                    if(drive.orientation=="up") drive.FireDrive(power_tdo,true);
                foreach(MergeDrive drive in MergeDrives) 
                    if(drive.orientation=="down") drive.FireDrive(power_tdo);
            }
            
            if(ShipReference.MoveIndicator.Z==1)    
            {
                foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="backward") drive.FireDrive(power_tba);
                foreach(MassDrive drive in MassDrives)
                    if(drive.orientation=="backward") drive.FireDrive(power_tba,false);
                foreach(MassDrive drive in MassDrives) 
                    if(drive.orientation=="forward") drive.FireDrive(power_tba,true);
                foreach(MergeDrive drive in MergeDrives) 
                    if(drive.orientation=="backward") drive.FireDrive(power_tba);
            }
            if(ShipReference.MoveIndicator.Z==-1) 
            {
                foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="forward") drive.FireDrive(power_tfo);
                foreach(MassDrive drive in MassDrives)
                    if(drive.orientation=="forward") drive.FireDrive(power_tfo,false);
                foreach(MassDrive drive in MassDrives) 
                    if(drive.orientation=="backward") drive.FireDrive(power_tfo,true);
                foreach(MergeDrive drive in MergeDrives) 
                    if(drive.orientation=="forward") drive.FireDrive(power_tfo);
            }
            
            
            if(dampeningstrength>0)     //dampeners can be turned off manually in the script
            {
                if(ShipReference.DampenersOverride) //if inertial dampeners are turned on ('Z' key), dampen speed in all unused axis
                {
                    if(ShipReference.GetShipSpeed()>0.05) InertialDampen();  //only dampen if speed above 0.05 m/s, it's useless below anyways
                }
            }
            
            foreach(PistonDrive drive in PistonDrives) if(drive.active==false) drive.StopDrive();   //shut off unused drives
            foreach(MergeDrive drive in MergeDrives) if(drive.active==false) drive.StopDrive(); 
        }
        else if(mode=="manual")     //drive handled by arguments
        {
            foreach(MergeDrive drive in MergeDrives) if(drive.orientation=="forward") drive.FireDrive(power); //fires only the forwards drives in manual mode if toggled on
            foreach(MassDrive drive in MassDrives) if(drive.orientation=="forward") drive.FireDrive(power,false); //fires only the forwards drives in manual mode if toggled on
            foreach(MassDrive drive in MassDrives) if(drive.orientation=="backward") drive.FireDrive(power,true);
            foreach(PistonDrive drive in PistonDrives) if(drive.orientation=="forward") drive.FireDrive(power);
        }
    }
}

void InertialDampen()
{
    //inertial dampening, when no input on axis, fire drives to reduce speed in all axis to zero
    
    //creates vector with Controller values based on the local ship speed 
    Vector3D PID_Local = new Vector3D(myControllerX.CalcValue(speedLocal.X),myControllerY.CalcValue(speedLocal.Y),myControllerZ.CalcValue(speedLocal.Z));
    
    if(ShipReference.MoveIndicator.X==0)    //right left axis, only dampen when axis is not used by WASD
    {
        if(PID_Local.X<0)
        {
            //adjusts power/retraction distance by Control value from PID_Controller
            foreach(MergeDrive drive in MergeDrives) 
                if(drive.orientation=="right") drive.FireDrive(dampeningstrength*power_tri*Math.Abs(PID_Local.X));
            foreach(PistonDrive drive in PistonDrives) 
                if(drive.orientation=="right") drive.FireDrive(dampeningstrength*power_tri*Math.Abs(PID_Local.X));
            foreach(MassDrive drive in MassDrives)
                if(drive.orientation=="right") drive.FireDrive(dampeningstrength*power_tri*Math.Abs(PID_Local.X),false);
            foreach(MassDrive drive in MassDrives) 
                if(drive.orientation=="left") drive.FireDrive(dampeningstrength*power_tri*Math.Abs(PID_Local.X),true);
        }
        else if(PID_Local.X>0)
        {
            foreach(MergeDrive drive in MergeDrives)
                if(drive.orientation=="left") drive.FireDrive(dampeningstrength*power_tle*Math.Abs(PID_Local.X));
            foreach(PistonDrive drive in PistonDrives) 
                if(drive.orientation=="left") drive.FireDrive(dampeningstrength*power_tle*Math.Abs(PID_Local.X));
            foreach(MassDrive drive in MassDrives)
                if(drive.orientation=="left") drive.FireDrive(dampeningstrength*power_tle*Math.Abs(PID_Local.X),false);
            foreach(MassDrive drive in MassDrives) 
                if(drive.orientation=="right") drive.FireDrive(dampeningstrength*power_tle*Math.Abs(PID_Local.X),true);
        }
    }
    if(ShipReference.MoveIndicator.Y==0)    //up down axis
    {
        if(PID_Local.Y>0)
        {
            foreach(MergeDrive drive in MergeDrives) 
                if(drive.orientation=="down") drive.FireDrive(dampeningstrength*power_tdo*Math.Abs(PID_Local.Y));
            foreach(PistonDrive drive in PistonDrives) 
                if(drive.orientation=="down") drive.FireDrive(dampeningstrength*power_tdo*Math.Abs(PID_Local.Y));
            foreach(MassDrive drive in MassDrives)
                if(drive.orientation=="down") drive.FireDrive(dampeningstrength*power_tdo*Math.Abs(PID_Local.Y),false);
            foreach(MassDrive drive in MassDrives) 
                if(drive.orientation=="up") drive.FireDrive(dampeningstrength*power_tdo*Math.Abs(PID_Local.Y),true);
        }
        else if(PID_Local.Y<0)
        {
            foreach(MergeDrive drive in MergeDrives) 
                if(drive.orientation=="up") drive.FireDrive(dampeningstrength*power_tup*Math.Abs(PID_Local.Y));
            foreach(PistonDrive drive in PistonDrives) 
                if(drive.orientation=="up") drive.FireDrive(dampeningstrength*power_tup*Math.Abs(PID_Local.Y));
            foreach(MassDrive drive in MassDrives)
                if(drive.orientation=="up") drive.FireDrive(dampeningstrength*power_tup*Math.Abs(PID_Local.Y),false);
            foreach(MassDrive drive in MassDrives) 
                if(drive.orientation=="down") drive.FireDrive(dampeningstrength*power_tup*Math.Abs(PID_Local.Y),true);
        }
    }
    if(ShipReference.MoveIndicator.Z==0)    //forward backward axis
    {
        if(PID_Local.Z<0)
        {
            foreach(MergeDrive drive in MergeDrives) 
                if(drive.orientation=="backward") drive.FireDrive(dampeningstrength*power_tba*Math.Abs(PID_Local.Z));
            foreach(PistonDrive drive in PistonDrives) 
                if(drive.orientation=="backward") drive.FireDrive(dampeningstrength*power_tba*Math.Abs(PID_Local.Z));
            foreach(MassDrive drive in MassDrives)
                if(drive.orientation=="backward") drive.FireDrive(dampeningstrength*power_tba*Math.Abs(PID_Local.Z),false);
            foreach(MassDrive drive in MassDrives) 
                if(drive.orientation=="forward") drive.FireDrive(dampeningstrength*power_tba*Math.Abs(PID_Local.Z),true);
        }
        else if(PID_Local.Z>0)
        {
            foreach(MergeDrive drive in MergeDrives) 
                if(drive.orientation=="forward") drive.FireDrive(dampeningstrength*power_tfo*Math.Abs(PID_Local.Z));
            foreach(PistonDrive drive in PistonDrives) 
                if(drive.orientation=="forward") drive.FireDrive(dampeningstrength*power_tfo*Math.Abs(PID_Local.Z));
            foreach(MassDrive drive in MassDrives)
                if(drive.orientation=="forward") drive.FireDrive(dampeningstrength*power_tfo*Math.Abs(PID_Local.Z),false);
            foreach(MassDrive drive in MassDrives) 
                if(drive.orientation=="backward") drive.FireDrive(dampeningstrength*power_tfo*Math.Abs(PID_Local.Z),true);
        }
    }
}


//class for all merge drives (MPD-1, not MPD-2x4(old))
public class MergeDrive
{
    private List<IMyMotorBase> Rotors; public List<IMyShipMergeBlock> MergeBlocks; public IMyShipConnector MainConnector, SubConnector; int tick, wiggle; public string orientation;
    public bool active; private IMyMotorBase MainRotor;
    
    public MergeDrive(List<IMyMotorBase> protors, List<IMyShipMergeBlock> pmerge, IMyShipConnector pmainconnector,IMyShipConnector pconnector, IMyMotorBase pmainrotor) //contructor to asign rotors and merge blocks and figure out orientation
    {
        wiggle=0;
        orientation="not defined";
        tick=3;     //drive needs 2 ticks to fire, 1 for extending rotor, 1 for retracting
        Rotors = protors;
        MainRotor = pmainrotor;
        MergeBlocks = pmerge;
        MainConnector = pmainconnector;
        SubConnector = pconnector;
    }

    public bool CheckDestruction()  //also reattaches all rotors and connectors
    {
        bool isdead = false;
        
        if(MainConnector==null || MainConnector.CubeGrid.GetCubeBlock(MainConnector.Position) == null) isdead=true;    //connector missing, drive is dead
        else if(MainConnector.IsFunctional==false) isdead=true; //damaged, also dead
        else if(MainConnector.Status==MyShipConnectorStatus.Unconnected) isdead=true;   //connector not connectable, drive is dead
        else if(MainConnector.Status==MyShipConnectorStatus.Connectable) MainConnector.Connect();   //connector connected, no need to check other connector
        
        if(MainRotor==null || MainRotor.CubeGrid.GetCubeBlock(MainRotor.Position) == null) isdead=true;    //main rotor missing, drive is dead
        else if(MainRotor.IsFunctional==false) isdead=true; //damaged, also dead
        else 
        {
            MainRotor.Attach();     //tries to reattach if not already
            if (!MainRotor.IsAttached) isdead=true;     //rotor still not attached
        }
        
        for(int i=0; i<Rotors.Count;i++) if(Rotors[i]==null || Rotors[i].CubeGrid.GetCubeBlock(Rotors[i].Position) == null) isdead=true;
        for(int i=0; i<MergeBlocks.Count;i++) if(MergeBlocks[i]==null || MergeBlocks[i].CubeGrid.GetCubeBlock(MergeBlocks[i].Position) == null) isdead=true;
        //either rotor or merge missing from stacks
        if(isdead==false)
        {
            for(int i=0; i<Rotors.Count;i++) if(Rotors[i].IsFunctional==false) isdead=true;
            for(int i=0; i<MergeBlocks.Count;i++) if(MergeBlocks[i].IsFunctional==false) isdead=true;
        }
        //either rotor or merge damaged
        if(isdead==false)
        {
            for(int i=0; i<Rotors.Count;i++) 
            {
                Rotors[i].Attach();
                if (!Rotors[i].IsAttached) isdead=true;
            }
        }
        //any rotor from stack is not attached
        
        return isdead;   //default response, drive is working fine
    }

    public void FigureOrientation(IMyCockpit ShipController)
    {
        Vector3 Distance = new Vector3(0,0,0);
        if(MainConnector.Orientation.Up==ShipController.Orientation.Forward || MainConnector.Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Forward))
        {
            Distance=Base6Directions.GetVector(ShipController.Orientation.Forward);
            if(MainRotor.Position==MainConnector.Position+(Distance)) orientation="backward";
            else if(MainRotor.Position==MainConnector.Position-(Distance)) orientation="forward";
        }
        else if(MainConnector.Orientation.Up==ShipController.Orientation.Up || MainConnector.Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Up))
        {
            Distance=Base6Directions.GetVector(ShipController.Orientation.Up);
            if(MainRotor.Position==MainConnector.Position+(Distance)) orientation="down";
            else if(MainRotor.Position==MainConnector.Position-(Distance)) orientation="up";
        }
        else if(MainConnector.Orientation.Up==ShipController.Orientation.Left || MainConnector.Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Left))
        {
            Distance=Base6Directions.GetVector(ShipController.Orientation.Left);
            if(MainRotor.Position==MainConnector.Position+(Distance)) orientation="right";
            else if(MainRotor.Position==MainConnector.Position-(Distance)) orientation="left";
        }
    }
    
    private void Wiggle()
    {
        if(wiggle<6) 
        {
            wiggle++; 
            for(int i=0; i<MergeBlocks.Count;i++) MergeBlocks[i].Enabled=true;
        }
        else 
        {
            wiggle=0; 
            for(int i=0; i<MergeBlocks.Count;i++) if(MergeBlocks[i].IsConnected==false) MergeBlocks[i].Enabled=false;
        }
    }
    
    public void FireDrive(double pMag)      //overload of FireDrive with adjusted retraction distance for less power, is used for inertial dampening, also needs axis to give to pid_controller
    {
        active=true;
        if(tick>3) tick--;
        switch(tick)
        {
            case 3:
                for(int i=0; i<MergeBlocks.Count;i++) MergeBlocks[i].Enabled=true;
                Wiggle();   //helps link merge blocks faster
                bool tempconnected = true;
                for(int i=0; i<MergeBlocks.Count;i++) if(MergeBlocks[i].IsConnected==false) tempconnected=false;    //only shoot out if all merge blocks are linked
                
                if(Rotors[0].GetValue<float>("Displacement")>-0.35) 
                {
                    tick--;     //skips if rotors are not fully retracted for some reason (and then rully retract them)
                    break;
                }
                
                if(tempconnected == true)        //Drive shoots out if all merge blocks are linked
                {
                   tick--;
                   extend(Rotors, (float) (0.5 * pMag));    //extend rotors with pMag as percentage
                }
                break;
                
            case 2:
                for(int i=0; i<MergeBlocks.Count;i++) MergeBlocks[i].Enabled=false;   //turn off merge block, else backwards acceleration
                retract(Rotors, (float) ( (Rotors[0].GetValue<float>("Displacement")+0.4)/2));  //retract half the remaining way
                tick--; //reset firing cycle
                break;
                
            case 1:
                retract(Rotors, (float) ((Rotors[0].GetValue<float>("Displacement")+0.4)/2));   //retract half the remaining way, split over two ticks for safety
                tick=3; //reset firing cycle
                break;
        }
    }
    
    public void StopDrive()
    {
        active=false;
        for(int i=0; i<MergeBlocks.Count;i++) MergeBlocks[i].Enabled=false;
    }

    private void extend (List<IMyMotorBase> roto, float travel)
    {
        for(int i = 0; i < roto.Count; i++) 
        {
            roto[i].SetValue("Displacement", roto[i].GetValue<float>("Displacement")+ (float) travel);      //extends by travel
        }
    }

    private void retract (List<IMyMotorBase> roto, float travel)
    {
        for(int i = 0; i < roto.Count; i++) 
        {
            roto[i].SetValue("Displacement", roto[i].GetValue<float>("Displacement")- (float) travel);     //retracts by travel
        }   
    }  
}

//class for all piston drives (PDD-3) and (PDD-1)
public class PistonDrive
{
    public bool active; public IMyDoor door; public bool hangar;
    public string orientation; public IMyExtendedPistonBase Piston;
        
    public PistonDrive(IMyExtendedPistonBase pPiston, IMyDoor pdoor, bool phangar) //overload with door, used for hangar door version
    {
        Piston = pPiston;
        door = pdoor;
        hangar=phangar;
        orientation="not defined";
        
        if(Piston.Top==null) Piston.GetActionWithName("Add Top Part").Apply(Piston);
        
        //move drive into start position, maxlimit is adjusted in startdrive method on the fly
        if(hangar==true)
        {
            Piston.MaxLimit=(float) 4.65;
            Piston.MinLimit=(float) 4.65;
            if(Piston.CurrentPosition<4.65) Piston.Velocity=5;
            if(Piston.CurrentPosition>4.65) Piston.Velocity=-5;
            door.CloseDoor();
        }
        else
        {
            Piston.MaxLimit=(float) 2.1;
            Piston.MinLimit=(float) 2.1;
            if(Piston.CurrentPosition<2.1) Piston.Velocity=5;
            if(Piston.CurrentPosition>2.1) Piston.Velocity=-5;
        }
        
        //Piston.MaxImpulseAxis=(float) 5000000;        //this property is inaccesible for some reason
        //Piston.IncreaseMaxImpulseAxis();              //method is also broken :(
    }
    
    public void FigureOrientation(IMyCockpit ShipController)
    {
        if(Piston.Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Forward)) orientation="forward";
        if(Piston.Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Up)) orientation="up";
        if(Piston.Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Left)) orientation="left";
        if(Piston.Orientation.Up==ShipController.Orientation.Forward) orientation="backward";   //Piston is pointing in direction of thrust, so pointing forward=backward thrust
        if(Piston.Orientation.Up==ShipController.Orientation.Up) orientation="down";
        if(Piston.Orientation.Up==ShipController.Orientation.Left) orientation="right";
    }
    
    public void FireDrive(double ppower)
    {
        active=true;
        if(hangar==false)
        {
            if(Piston.MaxLimit>(float) (2.31+0.19*ppower)) Piston.Velocity=-5;    //if limit is lowered, move back piston
            else Piston.Velocity=5;     //if limit is the same or higher, move out piston
        
            Piston.MaxLimit=(float) (2.31+0.19*ppower); //calc new maxlimit
        }
        else
        {
            if(Piston.MaxLimit>(float) (4.90+0.2*ppower)) Piston.Velocity=-5;    //if limit is lowered, move back piston
            else Piston.Velocity=5;     //if limit is the same or higher, move out piston
        
            Piston.MaxLimit=(float) (4.90+0.2*ppower); //calc new maxlimit
        }
    }
    
    public void StopDrive()
    {
        Piston.Velocity=-5;
    }
    
    public bool CheckDestruction()
    {
        bool isdead = false;
        
        if(Piston==null || Piston.CubeGrid.GetCubeBlock(Piston.Position) == null) isdead=true; //blocks gone, drive dead
        else if(Piston.IsFunctional==false) isdead=true; //blocks are damaged, drive also dead
        
        if(door==null || door.CubeGrid.GetCubeBlock(door.Position) == null) isdead=true;
        else if(door.IsFunctional==false) isdead=true;
        return isdead;   //default response, drive is working fine
    }
}

//class for all mass shift drives (MSD-2-S & MSD-2-21 & MSD-35) 
public class MassDrive
{
    public bool active; double revpower; public bool reverse; double safety;
    public List<IMyMotorBase> Rotors; int tick; public string orientation; int deadmass;
    public IMyTerminalBlock BaseBlock, TopBlock; double maxdisplacement;
       
    public MassDrive(List<IMyMotorBase> pRotors, IMyTerminalBlock pBase, IMyTerminalBlock pTop, double prevpower) //contructor to asign rotors and merge blocks and figure out orientation
    {
        revpower = prevpower;
        orientation="not defined";
        tick=2;     //drive needs 2 ticks to fire, 1 for extending rotor, 1 for retracting
        Rotors = pRotors;
        BaseBlock=pBase;
        TopBlock=pTop;
        deadmass = 6000;    //6 tons will stay in the moving cargo container to reach resonant frequency at 60 ticks per second, seems to work pretty well for all configs of rotors
        
        maxdisplacement = FigureMaxDisplacement();
        
        if(maxdisplacement > 0.5) //is large rotor
        {
            safety = 0.5;     //some rotors can't handle full power
        }
        else //is small rotor, or large rotor with small head
        {
            safety = 1;    //others can, like small adv rots
        }
        
        extend(Rotors, (float) (maxdisplacement*safety));   //extend half the way, to prevent big boom on activation
    }
    
    public float FigureMaxDisplacement()
    {
        float tempold = Rotors[0].GetValue<float>("Displacement");
        Rotors[0].SetValue("Displacement",(float) 100);
        float temphigh = Rotors[0].GetValue<float>("Displacement");
        Rotors[0].SetValue("Displacement", (float) -100);
        float templow = Rotors[0].GetValue<float>("Displacement");
        Rotors[0].SetValue("Displacement",(float) tempold);
        
        return temphigh-templow;
    }
    
    public void FireDrive(double pMag, bool preverse)        //overload of FireDrive with adjusted retraction distance for less power, is used for inertial dampening, also needs axis to give to pid_controller
    {
        active=true;
        if(preverse!=reverse) { if(tick==2) {tick=1;} else tick=2;}     //flip ticks, if reverse mode changes
        
        reverse=preverse;
        
        int totalmass = (int) TopBlock.GetInventory().CurrentMass;
        
        if(preverse==true)  //drive is working in reverse
        {
            switch(tick)
            {
                case 2:
                    transfer(TopBlock.GetInventory(),BaseBlock.GetInventory(), totalmass-deadmass); 
                    retract(Rotors, (float) (maxdisplacement * safety * pMag * revpower));    //retract rotors with pMag as percentage (0.5 because drive cant handle full power)
                    tick--;
                    break;
                case 1: 
                    transfer(BaseBlock.GetInventory(),TopBlock.GetInventory(), 999999999);
                    extend(Rotors, (float) (maxdisplacement));   //extend all the way
                    tick=2;
                    break;
            }
        }
        else    //drive is working normal
        {
            switch(tick)
            {
                case 2:
                    transfer(BaseBlock.GetInventory(),TopBlock.GetInventory(), 999999999);
                    retract(Rotors, (float) (maxdisplacement * safety *pMag));     //retract rotors with pMag as percentage (0.25 because drive cant handle full power)
                    tick--;
                    break;
                case 1: 
                    transfer(TopBlock.GetInventory(),BaseBlock.GetInventory(), totalmass-deadmass);     //tranfer all back
                    extend(Rotors, (float) (maxdisplacement));   //extend all the way
                    tick=2;
                    break;
            }
        }
    }
    
    void transfer(IMyInventory a, IMyInventory b, int pamount) 
    {
        a.TransferItemTo(b,0,0,true,pamount);     
    }

    public bool CheckDestruction()
    {
        bool isdead = false;
        
        for(int i=0; i<Rotors.Count; i++) if(Rotors[i]==null || Rotors[i].CubeGrid.GetCubeBlock(Rotors[i].Position)==null) isdead=true; //checks if any rotors are missing
        if(BaseBlock==null || BaseBlock.CubeGrid.GetCubeBlock(BaseBlock.Position)==null) isdead=true;   //checks if baseblock is missing
        else if(TopBlock==null || TopBlock.CubeGrid.GetCubeBlock(TopBlock.Position)==null) isdead=true; //checks if TopBlock is missing
        
        if(isdead==false) //all blocks there?
        {
            if(BaseBlock.IsFunctional==false || TopBlock.IsFunctional==false) isdead=true;            //blocks are damaged, drive also dead
            else {for(int i=0; i<Rotors.Count; i++) {if(Rotors[i].IsFunctional==false) isdead=true;}}
            if(TopBlock.GetInventory().IsConnectedTo(BaseBlock.GetInventory())==false) isdead=true;     //inventories not connected, also dead
        }
        return isdead;   //default response, drive is working fine
    }

    public void FigureOrientation(IMyCockpit ShipController)
    {
        if(Rotors[0].Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Forward)) orientation="forward";
        if(Rotors[0].Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Up)) orientation="up";
        if(Rotors[0].Orientation.Up==Base6Directions.GetOppositeDirection(ShipController.Orientation.Left)) orientation="left";
        if(Rotors[0].Orientation.Up==ShipController.Orientation.Forward) orientation="backward";   //Connector is pointing in direction of thrust, so pointing forward=backward thrust
        if(Rotors[0].Orientation.Up==ShipController.Orientation.Up) orientation="down";
        if(Rotors[0].Orientation.Up==ShipController.Orientation.Left) orientation="right";
    }
    
    private void extend (List<IMyMotorBase> roto, float travel)
    {
        for(int i = 0; i < roto.Count; i++) 
        {
            roto[i].SetValue("Displacement", roto[i].GetValue<float>("Displacement")+ (float) travel);      //extends by travel
        }
    }

    private void retract (List<IMyMotorBase> roto, float travel)
    {
        for(int i = 0; i < roto.Count; i++) 
        {
            roto[i].SetValue("Displacement", roto[i].GetValue<float>("Displacement")- (float) travel);     //retracts by travel
        }   
    }  
}

//class for all merge dampeners (MID-6-3, not MID-6(old) or MID-6-2(old))
public class MergeDampener
{
    public List<IMyMotorBase> Rotors; public List<IMyShipMergeBlock> MergeBlocks; public string orientation;
    
    public MergeDampener(List<IMyShipMergeBlock> pmerge, List<IMyMotorBase> protors)
    {
        orientation="not defined";
        Rotors = protors;
        MergeBlocks = pmerge;
    }
    
    public void FigureOrientation(IMyCockpit ShipController)
    {
        if(Rotors[0].Orientation.Up==ShipController.Orientation.Forward) orientation="correct direction";
        else orientation="wrong direction";
    }
    
    public bool CheckDestruction()
    {
        bool isdead = false;
        
        for(int i=0; i<Rotors.Count; i++) if(Rotors[i]==null || Rotors[i].CubeGrid.GetCubeBlock(Rotors[i].Position)==null) isdead=true; //checks if any rotors are missing
        for(int i=0; i<MergeBlocks.Count;i++) if(MergeBlocks[i]==null || MergeBlocks[i].CubeGrid.GetCubeBlock(MergeBlocks[i].Position) == null) isdead=true;    //checks if any merge is missing, will reinitialize with fewer merges
        
        if(isdead==false) //all blocks there?
        {
            for(int i=0; i<MergeBlocks.Count; i++) if(MergeBlocks[i].IsFunctional==false) isdead=true;            //blocks are damaged, drive also dead
            for(int i=0; i<Rotors.Count; i++) if(Rotors[i].IsFunctional==false) isdead=true;
        }
        return isdead;   //default response, drive is working fine
    }
}

//class for Controlling velocity for inertial dampening, is a parallel PID controller (I think)
public class PID_Controller
{
    private double error, ki, kp, kd, i;
    
    public PID_Controller(double proportional, double integral, double derivative)
    {
        ki= integral;
        kp = proportional;
        kd = derivative;
        error=0;
        i=0;
    }
    
    public double CalcValue(double perror)
    {
        double p = kp * perror;         //simple error as factor    |   all values always positive, separated by direction anyways
        if(p>2) p=2;    //clamp proportional value, priority over all others
        if(p<-2) p=-2;
        
        i += ki * perror;           //sum of all past errors, should converge on zero
        if(i>0.08) i=0.08;  //clamp integral value, somewhat priorized over differential
        if(i<-0.08) i=-0.08;
        
        double d = kd * (perror-error);     //change in error   | can get negative if error is falling fast
        if(d>1) d=1;    //clamp differential value
        if(d<-1) d=-1;
        
        
        error=perror;       //save error for derivative of next iteration
        
        double temp = p + i + d;
        if(temp > 1) temp = 1;
        if(temp < -1) temp = -1;    //clamp return
        
        
        return (temp);      //return sum of factors
    }
}