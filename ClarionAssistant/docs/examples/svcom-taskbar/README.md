# ITaskbarList3 — Windows Taskbar progress & thumbnail (SVCOM example)

A Clarion class that drives the Windows 7+ Taskbar button through the
`ITaskbarList3` COM interface using Clarion's bundled **SVCOM** "nano-COM"
support (`SVCOM.INC` / `SVCOM.CLW` in `\Clarion\libsrc\win`). No registration,
no manifest, no OLE control on a window — it is pure interface-pointer COM.

This is the worked example for **issue #39** and the companion to
[`docs/clarion-svcom-nanocom.md`](../../clarion-svcom-nanocom.md), which explains
the general pattern so you can wrap other interfaces (e.g. `IFileOpenDialog`).

## Credit

The original `CBTaskbarListCOMCls.INC/.CLW` (progress bar support) is
**© 2018 Carl Barnes**, contributed on issue #39. The files here are his, with
the **thumbnail** methods completed (the originals left them as `_Not_Done_`
stubs). Carl's progress code is unchanged.

## What works vs. what is new

| Feature | Method | Status |
|---|---|---|
| Determinate progress | `SetProgressValue(completed, total)` | Carl — proven (ships an .exe in the issue zip) |
| Progress state (normal/error/paused) | `SetProgress_PROGRESS(1\|2\|3)` | Carl — proven |
| Marquee / indeterminate | `SetProgress_INDETERMINATE()` | Carl — proven |
| Clear progress | `SetProgress_NOPROGRESS()` | Carl — proven |
| **Crop thumbnail to a rect** | **`SetThumbnailClip(l,t,r,b)`** | **New for #39 — compile & test** |
| **Restore full thumbnail** | **`ClearThumbnailClip()`** | **New for #39 — compile & test** |
| **Thumbnail tooltip** | **`SetThumbnailTooltip(text)`** | **New for #39 — compile & test** |
| **Status overlay icon** | **`SetOverlayIcon(hIcon[, desc])`** | **New for #39 — compile & test** |

> ⚠️ The new thumbnail methods are written to the exact SVCOM pattern Carl's
> proven progress methods use, but they have **not been compiled** (authored
> outside the IDE). Build the test app and verify before relying on them.

## Project defines (required by SVCOM)

```
_svLinkMode_=>1;_svDllMode_=>0      ! local (EXE / linked LIB)
! or for a DLL:  _svLinkMode_=>0;_svDllMode_=>1
```

## Usage

```clarion
   INCLUDE('CBTaskbarListCOMCls.INC'),ONCE     ! in global includes

Taskbar   CBTaskListApiClass                    ! data declaration

  CODE
  ! In EVENT:OpenWindow:
  Taskbar.Init(Window)                          ! finds the frame HWND, creates the COM object

  ! Progress (Carl):
  Taskbar.SetProgressValue(rowsDone, rowsTotal) ! 0..total fills the button
  Taskbar.SetProgress_PROGRESS(2)               ! turn the bar red (error)
  Taskbar.SetProgress_NOPROGRESS()              ! clear when finished

  ! Thumbnail (new — #39):
  Taskbar.SetThumbnailClip(8, 30, 208, 230)     ! show only that client rect in the preview
  Taskbar.ClearThumbnailClip()                  ! back to the whole window
  Taskbar.SetThumbnailTooltip('Importing orders…')
  Taskbar.SetOverlayIcon(hMyIcon, 'Busy')       ! pass 0 to remove
```

`Init`/`Destruct` own the COM lifetime (`CoInitialize` via `CCOMIniter`,
`CreateInstance`/`QueryInterface` via `CCOMObject`, and `Release` on teardown),
so callers only deal with native Clarion types.
