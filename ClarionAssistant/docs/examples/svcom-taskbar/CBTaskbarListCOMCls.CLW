!CBTaskbarListCOMCls (c) 2018 by Carl Barnes
      Member
      INCLUDE('SVCOM.INC'),ONCE
      Map
        module ('win32')
          OutputDebugString(*CString),RAW,PASCAL,NAME('OutputDebugStringA'),DLL(1)
          GetWindow(HWND SIGNED, UNSIGNED uCmd),HWND,PASCAL,DLL(1)    
          GetParent(HWND SIGNED),SIGNED,PASCAL,DLL(1)  
          GetAncestor(HWND SIGNED, UNSIGNED gaFlags),HWND,PASCAL,DLL(1) !1=Parent 2=Root 3=RootOwner  
        end
      End ! map
      Include('CBTaskbarListCOMCls.inc'),ONCE

!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.CONSTRUCT    PROCEDURE()
  CODE
  SELF.COMIniterCls &= NEW(CBCOMIniterClass)            !Does CoInitialized which is required 
  SELF.TaskbarListComObj &= NEW(CBCOMObjectClass)       !SV COM Object Class 
  IF ~SELF.COMIniterCls.IsInitialised() THEN
      SELF.InitHResult = SELF.COMIniterCls.LastHR()     !CoInitialize() failed to run 
  ELSE       
      SELF.InitHResult = HRESULT_CB_InitNotCalled          !If they never call Init they get this
  END 
  RETURN
!--------------------
CBTaskListApiClass.DESTRUCT    PROCEDURE()
  CODE                    
  IF NOT SELF.TaskbarListComObj &= NULL THEN
     SELF.SetProgress_NOPROGRESS()           !in case any progress is running
     IF NOT SELF.ITaskbarList3 &= NULL THEN  SELF.ITaskbarList3.Release().  
     DISPOSE(SELF.TaskbarListComObj)
  END      
  IF NOT SELF.COMIniterCls &= NULL THEN DISPOSE(SELF.COMIniterCls).
  SELF.InitWorked   = 0       
  RETURN
!--------------------
CBTaskListApiClass.Init     PROCEDURE(Window W)
HWnd        LONG,AUTO    
ParentHWnd  LONG,AUTO 
HR          HRESULT,AUTO 
    CODE
    HWnd = GetAncestor(W{PROP:Handle}, 3)   !3=ROOT Owner. THe Frame if MDI
    RETURN SELF.InitHWnd(HWnd)
!--------------------
CBTaskListApiClass.InitHWnd PROCEDURE(LONG HWnd) 
HR      HRESULT(S_OK)
HR2     HRESULT,AUTO
    CODE
    SELF.WndHandle = HWnd 
    SELF.InitWorked   = 0   
    SELF.LastHR       = 0
    SELF.InitHResult  = 0
    SELF.ITaskbarList3_IPtr = 0  
    IF ~HWnd THEN                   !TODO? IsWindow(HWnd)
       HR = HRESULT_CB_InitNoHWnd
    ELSIF ~SELF.COMIniterCls.IsInitialised() THEN  !Did CONSTRUCT CoInitialize() work?
        HR = SELF.COMIniterCls.LastHR()
    ELSE
        HR  = SELF.TaskbarListComObj.CreateInstance(ADDRESS(TaskbarList_ClsID),ADDRESS(_IUnknown)) !Cls ID added XP
        IF HR = S_OK THEN
           HR  = SELF.TaskbarListComObj.QueryInterface(ADDRESS(ITaskbarList3_IID),SELF.ITaskbarList3_IPtr)  !ITaskbarList3 added Win7
           IF HR = S_OK THEN
              SELF.ITaskbarList3 &= (SELF.ITaskbarList3_IPtr)
              HR2 = SELF.ITaskbarList3.HrInit()  !Docs say to call this. Should I care if it fails?
              IF HR2 < S_OK THEN SELF.DbgOut('ITaskbarList3.HrInit() failed HR = ' & HR &' = '& SELF.LongToHex(HR)) .
           END
        END
    END 
    IF HR <> S_OK THEN 
       SELF.LastHR       = HR
       SELF.InitHResult  = HR    
    ELSE 
       SELF.InitWorked = True   
?      ASSERT(SELF.IsInited(),'CBTaskListApiClass.Init completed but IsInited <<> True')
    END 
    RETURN S_Ok    
!--------------------
CBTaskListApiClass.IsInited PROCEDURE()!,BOOL
    CODE
    IF ~SELF.InitWorked THEN
        SELF.LastHR = SELF.InitHResult   !Assume caller will Return so LastHR way be of use
        RETURN False
    END
    !Assert some assumptions     
?   ASSERT(SELF.ITaskbarList3_IPtr<>0,'ITaskbarList3_IPtr=0')
?   ASSERT(NOT SELF.ITaskbarList3 &= NULL,'ITaskbarList3=NULL')
    RETURN True
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.SetProgress_PROGRESS       PROCEDURE(LONG Norm1_Error2_Pause3=1)!,BOOL,PROC  !TBPF_NORMAL TBPF_ERROR TBPF_PAUSED
TBPF_Equate     LONG,AUTO
    CODE
    TBPF_Equate = CHOOSE(Norm1_Error2_Pause3, TBPF_NORMAL,TBPF_ERROR,TBPF_PAUSED, TBPF_NORMAL)
    RETURN SELF.SetProgressState(TBPF_Equate)
!------------------------------------    
CBTaskListApiClass.SetProgress_Marquee        PROCEDURE()
    CODE
    RETURN SELF.SetProgress_INDETERMINATE()
CBTaskListApiClass.SetProgress_INDETERMINATE  PROCEDURE()
    CODE
    RETURN SELF.SetProgressState(TBPF_INDETERMINATE)
!------------------------------------    
CBTaskListApiClass.SetProgress_NOPROGRESS     PROCEDURE()
    CODE
    RETURN SELF.SetProgressState(TBPF_NOPROGRESS)
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.SetProgressState           PROCEDURE(LONG TBPF_Equate)
    CODE
    IF ~SELF.IsInited() THEN RETURN False.
    CLEAR(SELF.SPVLast)
    SELF.LastHR = SELF.ITaskbarList3.SetProgressState( SELF.WndHandle, TBPF_Equate)
    SELF.ProgressState = TBPF_Equate
    RETURN CHOOSE(SELF.LastHR>=S_Ok)  
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.GetProgressState PROCEDURE()!,LONG,PROTECTED
    CODE
    RETURN SELF.ProgressState
!----------------------------------------------    
CBTaskListApiClass.IsStateProgress PROCEDURE()!,LONG
    CODE
    CASE SELF.ProgressState
    OF TBPF_NORMAL OROF TBPF_ERROR OROF TBPF_PAUSED
       RETURN SELF.ProgressState    !True 
    END
    RETURN False
!----------------------------------------------       
CBTaskListApiClass.IsStateMarquee PROCEDURE()!,LONG
    CODE
    RETURN CHOOSE(SELF.ProgressState=TBPF_INDETERMINATE) 
    !--------------------------------------------------------------------------------------------------
CBTaskListApiClass.SetProgressValue PROCEDURE(LONG lCompleted, LONG lTotal)!,BOOL,PROC !Sets Progress Bar Position
!This COM method is prototyped as a ULONGLONG 64-bit interger.
! virtual HRESULT SetProgressValue(HWND hwnd, ULONGLONG ullCompleted, ULONGLONG ullTotal) = 0;
!     SetProgressValue PROCEDURE(SIGNED hwnd,      REAL ullCompleted,      REAL ullTotal)
!Under 32-bit Windows the 64-bit ULONGLONG is passed by Value like a REAL but inside is a INT64.
!You will find "typedef double ULONGLONG;" in the WinNT.h file. Maybe I'm wrong but this works.
Int64OverReal   GROUP,TYPE         
R64               REAL
I64               LIKE(INT64),OVER(R64)
                END
Completed64     LIKE(Int64OverReal)                
Total64         LIKE(Int64OverReal)
    CODE
    IF lCompleted = SELF.SPVLast.Completed AND lTotal= SELF.SPVLast.Total THEN  !if its the same why send the message
       SELF.LastHR = S_Ok 
       RETURN 1
    END     
    Completed64.I64.LO = lCompleted
    Total64.I64.LO     = lTotal
    SELF.LastHR = SELF.ITaskbarList3.SetProgressValue( SELF.WndHandle, Completed64.R64, Total64.R64) 
    IF SELF.LastHR=S_Ok THEN 
       SELF.SPVLast.Completed = lCompleted 
       SELF.SPVLast.Total     = lTotal
    END        
    RETURN CHOOSE(SELF.LastHR>=S_Ok)
!==================================================================================================
! Thumbnail features (added for issue #39). These follow the exact pattern proven by
! SetProgressState above: guard on IsInited(), call the interface method, map HRESULT to BOOL.
! NOTE: written to the SVCOM pattern but COMPILE-AND-TEST in the Clarion IDE before relying on it.
!==================================================================================================
CBTaskListApiClass.SetThumbnailClip PROCEDURE(LONG xLeft, LONG yTop, LONG xRight, LONG yBottom)!,BOOL,PROC
    CODE
    IF ~SELF.IsInited() THEN RETURN False.
    SELF.RectClip.xLeft   = xLeft           !RECT is client coords of the source window
    SELF.RectClip.yTop    = yTop
    SELF.RectClip.xRight  = xRight
    SELF.RectClip.yBottom = yBottom
    SELF.LastHR = SELF.ITaskbarList3.SetThumbnailClip(SELF.WndHandle, ADDRESS(SELF.RectClip))
    RETURN CHOOSE(SELF.LastHR >= S_Ok)
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.ClearThumbnailClip PROCEDURE()!,BOOL,PROC
    CODE
    IF ~SELF.IsInited() THEN RETURN False.
    SELF.LastHR = SELF.ITaskbarList3.SetThumbnailClip(SELF.WndHandle, 0)  !NULL prcClip = whole window
    RETURN CHOOSE(SELF.LastHR >= S_Ok)
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.SetThumbnailTooltip PROCEDURE(STRING TipText)!,BOOL,PROC
WideTip     CWideStr        !bSelfCleaning defaults TRUE, so Destruct frees the wide string on exit
pTip        LONG,AUTO
    CODE
    IF ~SELF.IsInited() THEN RETURN False.
    pTip = WideTip.Init(TipText)            !ANSI -> UTF-16LE; Init returns the LPCWSTR pointer
    SELF.LastHR = SELF.ITaskbarList3.SetThumbnailTooltip(SELF.WndHandle, pTip)
    RETURN CHOOSE(SELF.LastHR >= S_Ok)
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.SetOverlayIcon PROCEDURE(LONG hIcon, <STRING Description>)!,BOOL,PROC
WideDesc    CWideStr
pDesc       LONG
    CODE
    IF ~SELF.IsInited() THEN RETURN False.
    pDesc = 0                               !optional tooltip describing the overlay (accessibility)
    IF NOT OMITTED(2) THEN pDesc = WideDesc.Init(Description).
    SELF.LastHR = SELF.ITaskbarList3.SetOverlayIcon(SELF.WndHandle, hIcon, pDesc)  !hIcon=0 clears it
    RETURN CHOOSE(SELF.LastHR >= S_Ok)
!--------------------------------------------------------------------------------------------------
CBTaskListApiClass.DbgOut PROCEDURE(STRING DbgMsg)
cMsg    CSTRING(1024),AUTO
    CODE
    cMsg='CBTaskListApiClass: ' & CLIP(DbgMsg) & '<13,10>'
    OutputDebugString(cMsg)
    RETURN
!--------------------------------------------------------------------------------------------------    
CBTaskListApiClass.LongToHex       PROCEDURE(LONG L)!,STRING    !TO make HResults understandable
Lb      BYTE,DIM(4),OVER(L)
Hex     STRING('0123456789ABCDEF')
    CODE
    RETURN('0x' & |
           HEX[BSHIFT(Lb[4], -4) + 1] & HEX[BAND(Lb[4], 0FH) + 1] & |
           HEX[BSHIFT(Lb[3], -4) + 1] & HEX[BAND(Lb[3], 0FH) + 1] & |
           HEX[BSHIFT(Lb[2], -4) + 1] & HEX[BAND(Lb[2], 0FH) + 1] & |
           HEX[BSHIFT(Lb[1], -4) + 1] & HEX[BAND(Lb[1], 0FH) + 1] )
!------------------------------------------------------------------------------------
!------------------------------------------------------------------------------------
!------------------------------------------------------------------------------------
!==================================================================================== 
CBCOMIniterClass.LastHR PROCEDURE()!,HRESULT  !If ~IsInitialised() this gives HR fail code
    CODE
    RETURN SELF.HR 