OMIT('_EndOfInclude_',_CBTaskListApiClsInc_)
_CBTaskListApiClsInc_ EQUATE(1) 
!A class to implement Microsoft Windows Taskbar COM Object by Carl Barnes November 2018.
!The only feature is to display Progress in the Windows Taskbar so user can see progress when program doe snot have focus
!Requires Vista or maybe Windows 7. Others like XP will not crash, you just don't get a progress bar
!----------------------------------------------------------------------------
!How to use to display progress on the Taskbar
! Project Defines: _svLinkMode_=>1;_svDllMode_=>0 (local) or 0,1 (dll)
! Global Includes: INLCLUDE('CBTaskListApiCls.INC'),ONCE               
! Procedure Code:
! TaskListApiCls CBTaskListApiClass        !Data declare Class
! TaskListApiCls.Init(Window)              !In Event:OpenWindow call INIT
! To show progress:
!    TaskListApiCls.SetProgressValue( ProgCnt , 100)    !Pass Progress and ProgressRangeHigh
! more to come ....
! TaskListApiCls.SetProgress_PROGRESS()    
!TODO: This Class needs Defines or use  _svLinkMode_=>1;_svDllMode_=>0

!CBTaskbarListCOMCls (c) 2018 by Carl Barnes

   INCLUDE('SVCOM.INC'),ONCE
                                      
HRESULT_CB_InitNotCalled        EQUATE(0A0000099h)  !HResult if you never call Init
HRESULT_CB_InitNoHWnd           EQUATE(0A0000002h)  !HResult if could not find HWnd
HRESULT_CB_Unexpected           EQUATE(0A0000001h)  !HResult WTF happended?
HRESULT_ClsID_NotFound       EQUATE( 80040154h)     !REGDB_E_CLASSNOTREG
HRESULT_E_NOINTERFACE	     EQUATE( 80004002h)     !No such interface supported

TBPF_NOPROGRESS     EQUATE(0)
TBPF_INDETERMINATE  EQUATE(1)    !Cannot be used with Error or Paused
TBPF_Marquee        EQUATE(TBPF_INDETERMINATE)    !Carl defined
TBPF_NORMAL         EQUATE(2)
TBPF_ERROR          EQUATE(4)
TBPF_PAUSED         EQUATE(8)
                                 
CBTaskListApiClass    Class,Type,Module('CBTaskbarListCOMCls.Clw'),DLL(0),Link('CBTaskbarListCOMCls.Clw',1)
CONSTRUCT       PROCEDURE()
DESTRUCT        PROCEDURE()
Init            PROCEDURE(Window W),LONG,PROC   !Returns HResult S_OK=0=OK 
InitHWnd        PROCEDURE(LONG HWnd),LONG,PROC  !Returns HResult S_OK=0=OK 
IsInited        PROCEDURE(),BOOL
DbgOut          PROCEDURE(STRING DbgMsg)
LongToHex       PROCEDURE(LONG L),STRING        !HResults in Hex

SetProgress_PROGRESS       PROCEDURE(LONG Norm1_Error2_Pause3=1),BOOL,PROC   !TBPF_NORMAL TBPF_ERROR TBPF_PAUSED
SetProgress_INDETERMINATE  PROCEDURE(),BOOL,PROC  !TBPF_INDETERMINATE Turn On Marquee in Taskbar
SetProgress_Marquee        PROCEDURE(),BOOL,PROC  !TBPF_INDETERMINATE same 
SetProgress_NOPROGRESS     PROCEDURE(),BOOL,PROC  !TBPF_NOPROGRESS Turn Off in the Taskbar
SetProgressState           PROCEDURE(LONG TBPF_Equate),BOOL,PROC,PROTECTED   
ProgressState              LONG,PROTECTED         !What is running 
GetProgressState           PROCEDURE(),LONG       !Returns what's running NORMAL ERROR PAUSED INDETERMINATE
IsStateProgress            PROCEDURE(),LONG       !Returns 123 if running NORMAL ERROR PAUSED else zero
IsStateMarquee             PROCEDURE(),BOOL       !Returns 1 if running INDETERMINATE / Marquee

SPVLast             GROUP     !SetProgressValue last values to avoid calling with same
Completed               LONG  
Total                   LONG  
                    END 
SetProgressValue           PROCEDURE(LONG lCompleted, LONG lTotal),BOOL,PROC !Sets Progress Bar Position

!--- Thumbnail features (added for issue #39; mirror the SetProgressState pattern) ---
SetThumbnailClip           PROCEDURE(LONG xLeft, LONG yTop, LONG xRight, LONG yBottom),BOOL,PROC !Crop the live Taskbar thumbnail to a client rectangle
ClearThumbnailClip         PROCEDURE(),BOOL,PROC                 !Restore the full-window thumbnail (pass NULL rect)
SetThumbnailTooltip        PROCEDURE(STRING TipText),BOOL,PROC   !Tooltip shown when hovering the Taskbar thumbnail
SetOverlayIcon             PROCEDURE(LONG hIcon, <STRING Description>),BOOL,PROC !16x16 status overlay on the button; pass hIcon=0 to clear

COMIniterCls        &CBCOMIniterClass  !Constructor Calls CoInitialize()
TaskbarListComObj   &CBCOMObjectClass
WndHandle           LONG
DbMsgs              BOOL
LastHR              LONG
InitWorked          BOOL,PROTECTED
InitHResult         LONG
ITaskbarList3_IPtr  LONG    !Used during Init, may not be safe to use
ITaskbarList3       &ITaskbarList3_COM !,PROTECTED
RectClip            GROUP   !Reusable RECT passed to SetThumbnailClip (must outlive the call)
xLeft                 LONG
yTop                  LONG
xRight                LONG
yBottom               LONG
                    END
            END

!To get to protected data need to inherit SVCom
CBCOMIniterClass    Class(CCOMIniter),Type,Module('CBTaskbarListCOMCls.Clw'),DLL(0),Link('CBTaskbarListCOMCls.Clw',1)
LastHR                 PROCEDURE(),HRESULT  !If ~IsInitialised() this gives HR fail code
                    END 
CBCOMObjectClass    Class(CCOMObject),Type,Module('CBTaskbarListCOMCls.Clw'),DLL(0),Link('CBTaskbarListCOMCls.Clw',1)
!LastHR                 PROCEDURE(),HRESULT  !If ~IsInitialised() this gives HR fail code
                    END 

!=================  COM Definitions from ShObjIdl.h ============================
ITaskbarList_COM   INTERFACE(IUnknown),COM     ! MIDL_INTERFACE("56FDF342-FD6D-11d0-958A-006097C9A090") ITaskbarList : public IUnknown
HrInit          PROCEDURE(),LONG,PROC          ! HrInit( void)
AddTab          PROCEDURE(LONG hWnd),LONG,PROC ! AddTab(HWND hwnd)
DeleteTab       PROCEDURE(LONG hWnd),LONG,PROC ! DeleteTab(HWND hwnd)
ActivateTab     PROCEDURE(LONG hWnd),LONG,PROC ! ActivateTab(HWND hwnd)
SetActiveAlt    PROCEDURE(LONG hWnd),LONG,PROC ! SetActiveAlt(HWND hwnd)
                   END
                   
ITaskbarList2_COM   INTERFACE(ITaskbarList_COM),COM !MIDL_INTERFACE("602D4995-B13A-429b-A66E-1935E44F4317") ITaskbarList2 : public ITaskbarList
MarkFullscreenWindow    PROCEDURE(LONG hWnd, BOOL fFullscreen),LONG,PROC,RAW ! MarkFullscreenWindow(HWND hwnd, BOOL fFullscreen)
                   END

ITaskbarList3_COM   INTERFACE(ITaskbarList2_COM),COM  !    MIDL_INTERFACE("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")   ITaskbarList3 : public ITaskbarList2 
!SetProgressValue     PROCEDURE(SIGNED hwnd, ULONGLONG ullCompleted, ULONGLONG ullTotal),LONG,PROC,RAW
SetProgressValue      PROCEDURE(SIGNED hwnd, REAL ullCompleted, REAL ullTotal),LONG,PROC,RAW    ! typedef double ULONGLONG;
SetProgressState      PROCEDURE(SIGNED hwnd, LONG tbpFlags),LONG,PROC  ! SetProgressState(HWND hwnd, TBPFLAG tbpFlags)   

RegisterTab           PROCEDURE(BYTE _Not_Done_),LONG,PROC ! RegisterTab( HWND hwndTab,  HWND hwndMDI)
UnregisterTab         PROCEDURE(BYTE _Not_Done_),LONG,PROC ! UnregisterTab( HWND hwndTab)
SetTabOrder           PROCEDURE(BYTE _Not_Done_),LONG,PROC ! SetTabOrder( HWND hwndTab,  HWND hwndInsertBefore)
SetTabActive          PROCEDURE(BYTE _Not_Done_),LONG,PROC ! SetTabActive( HWND hwndTab,  HWND hwndMDI,    DWORD dwReserved)
ThumbBarAddButtons    PROCEDURE(BYTE _Not_Done_),LONG,PROC ! ThumbBarAddButtons( HWND hwnd, UINT cButtons, _ecount_full(cButtons) LPTHUMBBUTTON pButton)
ThumbBarUpdateButtons PROCEDURE(BYTE _Not_Done_),LONG,PROC ! ThumbBarUpdateButtons( HWND hwnd, UINT cButtons,     _ecount_full(cButtons) LPTHUMBBUTTON pButton)
ThumbBarSetImageList  PROCEDURE(BYTE _Not_Done_),LONG,PROC ! ThumbBarSetImageList( HWND hwnd, _opt HIMAGELIST himl)
SetOverlayIcon        PROCEDURE(LONG hwnd, LONG hIcon, LONG pszDescription),LONG,PROC ! SetOverlayIcon(HWND hwnd, HICON hIcon, LPCWSTR pszDescription)  [enabled for #39 — was _Not_Done_; slot order unchanged]
SetThumbnailTooltip   PROCEDURE(LONG hwnd, LONG pszTip),LONG,PROC ! SetThumbnailTooltip(HWND hwnd, LPCWSTR pszTip)  [enabled for #39 — was _Not_Done_; slot order unchanged]
SetThumbnailClip      PROCEDURE(LONG hWnd, LONG prcClip),LONG,PROC ! SetThumbnailClip( HWND hwnd, RECT *prcClip)
                END                       

TaskbarList_ClsID           GROUP  !DECLSPEC_UUID("56FDF344-FD6D-11d0-958A-006097C9A090") TaskbarList;
Data1                         long(56FDF344h)
Data2                         short(0FD6Dh)
Data3                         short(11d0h)
Data4                         string('<95h><8Ah><00h><60h><97h><0C9h><0A0h><90h>')
                            END
ITaskbarList3_IID           GROUP  !MIDL_INTERFACE("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf") ITaskbarList3 : ITaskbarList2 
Data1                         long(0ea1afb91h)     !1 2 3 4  5 6  7 8  9 10 111213141516         
Data2                         short(9e28h)
Data3                         short(4b86h)
Data4                         string('<90h><0E9h><9Eh><9Fh><8Ah><5Eh><0EFh><0AFh>')
                            END      !  9    10   11   12   13    14    15    16   
!--------------------------------------------------------------------------------

_EndOfInclude_