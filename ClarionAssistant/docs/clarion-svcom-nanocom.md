# Calling Windows COM Interfaces from Clarion with SVCOM ("nano-COM")

Many modern Windows APIs are exposed only as **COM interfaces** rather than flat
`MODULE('win32')` function prototypes — `ITaskbarList3` (taskbar progress &
thumbnails), `IFileOpenDialog`/`IFileSaveDialog` (the Vista+ file dialogs),
`IPropertyStore`, `IDesktopWallpaper`, and so on. These are sometimes called
**"nano-COM"** or **"COM-Lite"**: standard COM objects you create and call by
interface pointer, but with **no registration, no manifest, and no OLE/ActiveX
control on a window**.

Clarion ships everything needed to do this in **`SVCOM.INC` / `SVCOM.CLW`**
(in `\Clarion\libsrc\win`). This guide explains the reusable pattern; the
worked, runnable example is
[`docs/examples/svcom-taskbar/`](examples/svcom-taskbar/) (`ITaskbarList3`).

> This is **not** the OLE/ActiveX control pattern (`?OLE{PROP:Create}` +
> `OCXREGISTEREVENTPROC`), and **not** the .NET RegFree COM control pattern.
> It is raw interface-pointer COM against an OS-provided object.

---

## The mental model

A COM object is a block of memory whose first field points at a **vtable** — an
ordered array of function pointers. An *interface* is just an agreed-upon
**order** of those functions. Every COM interface derives from `IUnknown`, whose
first three slots are always `QueryInterface`, `AddRef`, `Release`. A derived
interface appends its own methods **after** its parent's, in declaration order.

In C/C++ you call `pInterface->lpVtbl->Method(pInterface, args)`. Clarion's
`INTERFACE(...),COM` does the vtable dispatch for you — **as long as you declare
the methods in exactly the same order as the C++ header.** Order is everything;
a wrong or missing slot calls the wrong function and crashes.

The typical workflow (same as C++, just in Clarion types):

1. **Initialize COM** for the thread — `CoInitializeEx`. SVCOM's `CCOMIniter`
   does this in its constructor.
2. **Create the object** — `CoCreateInstance(CLSID, IID_IUnknown)`. SVCOM's
   `CCOMObject.CreateInstance`.
3. **Query for the interface you want** — `QueryInterface(IID, &ptr)`. SVCOM's
   `CCOMObject.QueryInterface`.
4. **Bind a Clarion interface reference to the pointer** — `iface &= (ptr)`.
5. **Call methods** — `iface.Method(args)`.
6. **Release** every interface you obtained.
7. **Uninitialize** COM — handled by `CCOMIniter`'s destructor.

---

## The SVCOM substrate (what you build on)

From `SVCOM.INC`:

| Class | Use |
|---|---|
| `CCOMIniter` | Constructor calls `CoInitialize`; `IsInitialised()` gates everything. `NEW` one and keep it alive for the COM object's lifetime. |
| `CCOMObject` | `CreateInstance(rclsid, riid [,dwClsContext])`, `QueryInterface(riid, *ppvObject)`, `AddRef`, `Release`, `Attach`, `AssignPtr`. The create/query engine. |
| `CWideStr` | Build a Windows wide (UTF-16LE) string from a Clarion `STRING`/`CSTRING`. `Init()` returns the `LPCWSTR` pointer; `GetWideStr()` returns it again. **This is how you pass any `LPCWSTR` parameter.** |
| `CBStr` | Same idea for automation `BSTR` parameters. |
| `CStr` | Convert a wide/`BSTR` result back to a Clarion string. |

`HRESULT`, `S_OK`, `CLSCTX_ALL`, `IUnknown`, and the `_IUnknown` IID group are
defined by SVCOM as well.

**Required project defines** (link mode of SVCOM itself):

```
_svLinkMode_=>1;_svDllMode_=>0      ! local: EXE or linked LIB
_svLinkMode_=>0;_svDllMode_=>1      ! DLL
```

---

## Declaring an interface and its GUIDs

Declare the interface with `INTERFACE(parent),COM`, methods in vtable order.
Declare the CLSID (object id) and IID (interface id) as `GROUP`s laid out as a
Windows `GUID` — `Data1` LONG, `Data2`/`Data3` SHORT, `Data4` an 8-byte STRING.

```clarion
ITaskbarList_COM    INTERFACE(IUnknown),COM   ! 56FDF342-FD6D-11d0-958A-006097C9A090
HrInit          PROCEDURE(),LONG,PROC
AddTab          PROCEDURE(LONG hWnd),LONG,PROC
DeleteTab       PROCEDURE(LONG hWnd),LONG,PROC
ActivateTab     PROCEDURE(LONG hWnd),LONG,PROC
SetActiveAlt    PROCEDURE(LONG hWnd),LONG,PROC
                END

ITaskbarList2_COM   INTERFACE(ITaskbarList_COM),COM
MarkFullscreenWindow PROCEDURE(LONG hWnd, BOOL fFullscreen),LONG,PROC,RAW
                END

ITaskbarList3_COM   INTERFACE(ITaskbarList2_COM),COM
SetProgressValue     PROCEDURE(SIGNED hwnd, REAL ullCompleted, REAL ullTotal),LONG,PROC,RAW
SetProgressState     PROCEDURE(SIGNED hwnd, LONG tbpFlags),LONG,PROC
! ...the remaining slots, IN ORDER, even ones you don't implement yet...
                END

ITaskbarList3_IID   GROUP   ! ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf
Data1                 LONG(0ea1afb91h)
Data2                 SHORT(9e28h)
Data3                 SHORT(4b86h)
Data4                 STRING('<90h><0E9h><9Eh><9Fh><8Ah><5Eh><0EFh><0AFh>')
                    END
```

### Vtable-order rules that bite

- **Never reorder, insert, or delete** a method in an interface you don't fully
  implement — every method is one vtable slot. To leave a method unimplemented,
  keep a **placeholder of the same slot count**, e.g.
  `ThumbBarAddButtons PROCEDURE(BYTE _Not_Done_),LONG,PROC`. When you later
  implement it, swap the signature *in place* — the slot index is unchanged.
  (That is exactly how the thumbnail methods were enabled in the example:
  `SetThumbnailTooltip` went from `(BYTE _Not_Done_)` to `(LONG hwnd, LONG pszTip)`
  without moving it.)
- **`,PROC`** lets you ignore the returned `HRESULT` when you want to.
- **`,RAW`** passes a Clarion string/group as a bare address (no length word) —
  use it when a method takes a pointer/struct you're handing over as raw bytes.

---

## Mapping C++ types to Clarion

| C++ / IDL | Clarion | Notes |
|---|---|---|
| `HRESULT` | `LONG` (or `HRESULT`) | `>= 0` (`S_OK`) is success; `< 0` is failure |
| `HWND`, `HICON`, handles | `LONG` / `SIGNED` | 32-bit handle |
| `BOOL` | `BOOL` / `LONG` | |
| `UINT`, `DWORD` | `ULONG` / `LONG` | |
| `LPCWSTR` (wide string in) | `LONG` pointer | build with `CWideStr`, pass `Init()`/`GetWideStr()` |
| `BSTR` | `LONG` pointer | build with `CBStr` |
| `RECT *`, any `struct *` | `LONG` pointer | pass `ADDRESS(group)`, or `0` for `NULL` |
| `ULONGLONG` (by value, 32-bit) | `REAL` over `INT64` | the example's `SetProgressValue` trick: a 64-bit int passed by value occupies the same stack slot as a `REAL` |
| `REFIID`, `REFCLSID` | `LONG` pointer | pass `ADDRESS(iid_group)` |

---

## Worked example: `ITaskbarList3`

See [`docs/examples/svcom-taskbar/`](examples/svcom-taskbar/). The lifetime is
owned by a wrapper class so callers never touch COM directly:

```clarion
! CONSTRUCT: bring COM up
SELF.COMIniterCls      &= NEW(CBCOMIniterClass)     ! CoInitialize
SELF.TaskbarListComObj &= NEW(CBCOMObjectClass)

! Init: create + query + bind
HR = SELF.TaskbarListComObj.CreateInstance(ADDRESS(TaskbarList_ClsID), ADDRESS(_IUnknown))
HR = SELF.TaskbarListComObj.QueryInterface(ADDRESS(ITaskbarList3_IID), SELF.ITaskbarList3_IPtr)
SELF.ITaskbarList3 &= (SELF.ITaskbarList3_IPtr)     ! bind Clarion ref to the raw pointer
SELF.ITaskbarList3.HrInit()

! Call (note: hwnd is passed on every ITaskbarList3 call)
SELF.LastHR = SELF.ITaskbarList3.SetProgressState(SELF.WndHandle, TBPF_NORMAL)

! A LPCWSTR parameter, the SVCOM way:
pTip = WideTip.Init(TipText)                        ! ANSI -> UTF-16LE, returns the pointer
SELF.LastHR = SELF.ITaskbarList3.SetThumbnailTooltip(SELF.WndHandle, pTip)

! A struct* parameter (RECT), or NULL to clear:
SELF.LastHR = SELF.ITaskbarList3.SetThumbnailClip(SELF.WndHandle, ADDRESS(SELF.RectClip))
SELF.LastHR = SELF.ITaskbarList3.SetThumbnailClip(SELF.WndHandle, 0)

! DESTRUCT: release in reverse, then COM comes down with CCOMIniter
IF NOT SELF.ITaskbarList3 &= NULL THEN SELF.ITaskbarList3.Release().
```

Key lifetime rules:

- Keep the `CCOMIniter` alive as long as any interface pointer is alive.
- A struct you pass by address (the `RECT`) must **outlive the call** — make it a
  class member, not a stack local that's gone before the COM method runs.
- `Release()` every interface you `QueryInterface`/`CreateInstance`; do it in the
  destructor so it happens even on early returns.
- `CWideStr`/`CBStr` with `bSelfCleaning = TRUE` (the default) free themselves in
  their destructor — declare them local to the method and don't double-free.

---

## Applying this to another interface (e.g. `IFileOpenDialog`)

The recipe is identical; only the GUIDs, the method list, and the parameter
types change:

1. Find the interface in the Windows SDK header (`ShObjIdl_core.h`) or on MSDN.
   Note its **IID**, its **parent**, and its **CLSID** if you create it directly
   (`CLSID_FileOpenDialog` for `IFileOpenDialog`).
2. Transcribe every method **in vtable order** into an `INTERFACE(parent),COM`,
   mapping types with the table above. Stub unfinished slots with
   `(BYTE _Not_Done_)` placeholders so the order is preserved.
3. `CreateInstance(CLSID, IID_IUnknown)` → `QueryInterface(IID)` → bind → call.
4. Wrap it in a class that exposes native-Clarion methods (`STRING`, `LONG`,
   `*CSTRING` out-params) and hides `HRESULT`/`LPCWSTR`/pointer plumbing.

`IFileOpenDialog` is a good next target — it replaces `FileDialog()` with the
modern Vista+ dialog (custom controls, places, multi-select). Build the
interface, call `Show(hwndOwner)`, then `GetResult` → `IShellItem.GetDisplayName`
→ `CStr` back to a Clarion path.

---

## Gotchas checklist

- [ ] SVCOM link defines set (`_svLinkMode_` / `_svDllMode_`).
- [ ] `CoInitialize` done (via `CCOMIniter`) **before** any COM call, on the same thread.
- [ ] Interface methods declared in **exact vtable order**, parent methods included by inheriting the parent `INTERFACE`.
- [ ] GUID groups laid out `LONG, SHORT, SHORT, STRING(8)` with correct byte order in `Data4`.
- [ ] `LPCWSTR` built with `CWideStr` (not a plain `CSTRING` — these APIs are Unicode-only since Vista).
- [ ] Structs passed by `ADDRESS()` outlive the call; `0` means `NULL`.
- [ ] Every obtained interface `Release()`d; failures degrade gracefully (old OS just returns an error `HRESULT`, no crash).
