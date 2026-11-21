# Pixill: design notes

Why the program is shaped the way it is. The README says what Pixill does;
this says what was decided, what was tried, and what each decision cost.

## Where it came from

Pixill started as a memory question. On a 64 GB Windows 11 machine, sixteen
gigabytes were "in use" at idle and about ten could be accounted for by eye.
Measuring the rest turned up the Windows Widgets board: one host process and
five embedded-browser processes, about 770 MB between them, for a panel its
owner never opened. The only things on it that were ever wanted were a few
stock prices and the chance of rain.

So the brief was narrow from the start:

- a few stocks and a week of weather, nothing else;
- small on screen and small in memory;
- no work at all when it cannot be seen;
- sized relative to the display, not in fixed pixels;
- settings in a file that can be edited by hand.

Windows does let third parties add widgets to its own board, but only as
Adaptive Cards (templated JSON, no custom drawing) inside the same host that
was just measured. Modifying Explorer to get at the taskbar breaks on Windows
updates. An independent window on the desktop was the remaining option, and
the one Rainmeter proved long ago.

## One window, pinned to the bottom

The widget is an ordinary borderless top-level window that keeps itself at
the bottom of the z-order: every time Windows is about to change its
position, it answers "below everything". It is a tool window, so it has no
taskbar button and does not appear in Alt+Tab.

"Show desktop" is the awkward case. It does not minimise windows; it raises
the desktop above them, which would bury a bottom-pinned window. Pixill
listens for foreground changes, and when the desktop itself comes to the
front *and* is above the ordinary windows, it lifts the widget over it until
something else takes the foreground. Clicking the wallpaper also brings the
desktop to the foreground but does not raise it, which is how the two cases
are told apart. This path has had very little real-world testing.

Stocks and weather began as two windows. They were merged because two things
that are always read together should move, size and theme together, and
because a shared width looks deliberate where two nearly-equal widths look
like a mistake. The stock rows decide the width; the weather charts absorb
whatever is left.

## Doing nothing when hidden

Each data source ("feed") has its own interval. A single timer sleeps until
the next feed is due. When one comes due, the window asks how much of itself
is visible before doing anything else:

1. Walk the windows above this one, top to bottom.
2. Ignore the ones that cannot hide anything: invisible, minimised, cloaked
   (other virtual desktops, suspended store apps), click-through overlays,
   and fully transparent layered windows.
3. Subtract each remaining window's rectangle from the widget's rectangle.
4. If less than 10% of the widget's area is left, it is hidden.

Hidden and due means: do not fetch, look again in a second. That one-second
look touches no network and costs a walk of the window list. Visible and due
means fetch, then sleep until the next due time. While nothing is due there
is no polling of any kind.

The rain animation obeys the same test, re-checked about once a second
instead of on every frame, and it stops entirely when no rain is forecast for
the hours still ahead. Animation frames invalidate only the block of charts,
and the paint routine notices that and skips all text.

A locked session counts as hidden.

## Data

**Stocks** use the JSON endpoints behind Yahoo Finance's own pages: one for a
symbol's current price and previous close, one for search. They need no key
and no cookie. They are also undocumented, so the client is deliberately
thin and the README says plainly that they may change. The day change is
computed from the previous close the endpoint reports, and was checked
against Yahoo's own percentage on a day when the index fell 5.6%.

**Weather** uses Open-Meteo, which is free, key-less, and returns exactly the
shape needed: the temperature now, daily high, low and maximum chance of
rain, and hourly chance and amount, always starting at local midnight. That
last property is what allows the charts to go without hour labels. If the
data began at "now" instead, the position of a bar would no longer imply its
hour, and labels would be needed.

One practical trap: Open-Meteo answers a request that accepts "deflate" with
a zlib-wrapped stream that the .NET Framework's decoder rejects. The client
asks for gzip only.

## Layout in display units

Row height is given in *vh*, hundredths of the display's height, and capped
at 5. Everything else (padding, gaps, fonts, the pixel size of the graphics)
is derived from the row height, so one number scales the whole widget. The
window re-measures when it is dragged to a display of a different size.

### Why sizes come in steps

A pixel font drawn at a size that is not a whole multiple of its design grid
gets blurred or uneven pixels. Jersey 15 is designed on a 15-pixel grid, so
it is sharp at 15, 30, 45 pixels and nowhere between. Pixill therefore snaps
the requested text size to the nearest multiple, and it draws the trend
arrows, the temperature blocks and the raindrops with the same "retro pixel"
size as the text. On a 1440-pixel-high display that gives two natural sizes:
a row height of 2.4vh (font at 2x, graphics at 2 px) and 1.25vh (font at 1x,
graphics at 1 px), the second an exact half of the first. A sibling font with
a 10-pixel grid, Jersey 10, gives a two-thirds size and is included, but its
thinner strokes hurt the small text, so it is not the default.

Text is drawn with smoothing switched off for the same reason: an
anti-aliased pixel font is no longer a pixel font.

## The weather row

A row has to carry five facts in the height of one line of text: low, high,
chance of rain for the day, and chance and amount for each of 24 hours.

- **Low, bar, high.** The bar is a strip of blocks spanning the week's coldest
  low to its warmest high. The lit blocks are that day's range, each coloured
  by the temperature at its position on a fixed cold-to-hot ramp. Position
  shows how the day compares with the rest of the week; colour shows the
  temperature in absolute terms. Today's row adds a notch at the current
  temperature.
- **The chart.** Chance of rain grows up from the bottom; amount comes down
  from the top. Each is limited to half the row. The first version let both
  use the full height and overlap; halving them costs resolution but removes
  any hour where one hides the other, and it makes the middle line a shared
  limit that needs no left-hand scale: a full bottom half is 100%.
- **One scale for the week.** The amount that fills the top half is the same
  in every row and is printed once, in the header. The first default was
  1 inch per hour, which is a cloudburst; ordinary rain is a tenth of that
  and drew as a single pixel. The default became 0.25", about where heavy
  rain begins. If any hour in the week exceeds the base, the scale becomes
  the next whole step above the wettest hour, for all rows.
- **Falling rain.** The amount is drawn as a faint backdrop, which keeps the
  height readable, with one pixel drop per sub-column falling through it.
  Speed and drop length grow with the amount, so a heavy hour looks heavy
  before its height is read. Drops are phased by a hash of day, hour and
  column, with a pause between falls, so the motion stays sparse.
- **No day names, no clock.** The first row is today. Past hours of today are
  dimmed and the current hour is marked. That was judged enough.

## Colour

Windows exposes two usable things: a dark/light switch (two of them; Pixill
follows the one for the shell, since a desktop widget is furniture like the
taskbar) and one accent colour with three lighter and three darker shades.
The classic "system colours" still exist but are frozen at light-theme
defaults unless a contrast theme is active. Windows has no gain/loss colours
and no temperature colours.

That led to these rules:

- Neutrals (background, text, dividers, chart backgrounds) follow the
  dark/light switch, then lean 8% toward the accent. Using the accent
  outright as a background, as the taskbar can, was rejected because red and
  green figures on a strongly coloured ground lose their contrast.
- Green and red are fixed in meaning. Each has a dark-mode and a light-mode
  tone, and neither is ever taken from the theme.
- Rain was briefly tied to the accent. With a red accent, "100% rain" then
  looked like a loss. Rain now uses the system highlight colour, and the
  amount is that colour 20% lighter; where lightening would pass white, the
  overshoot is taken from the opacity instead.

Theme changes arrive as a system event and are applied without a restart.

## The logo

A 16x16 grid, because that is the tray's real size and a pixel drawing should
not be scaled by anything but whole numbers. It is an impression of a
four-pane window rather than a drawing of one: a one-pixel wooden frame with
the top corners cut in, bars with pixels missing, black night, five drops of
slanting rain placed unevenly on purpose, and a little fog along the bottom
of the lower panes. There is no sill. The widget's rows are the sill.

It was chosen over three rounds of rendered variants: first the rain, then
the frame with the rain fixed, then the highlight with both fixed. Varying
one thing at a time was what made a choice possible.

The grid lives in source. The tray icon is drawn from it at run time, and the
build asks the program to write the same grid out as an .ico for the exe.

## Implementation choices

**C# on Windows Forms, .NET Framework 4.8.** The Framework ships inside
Windows 10 and 11, so there is no runtime to install, and Windows also ships
a C# compiler, so there is nothing to install to build either. The code
stays within what that older compiler accepts. Drawing is done by hand
(GDI text, GDI+ rectangles); there is no layout framework and there are no
packages. The resulting program is a single exe of about 175 KB, most of it the
embedded font, and settles at about 50 MB of working set, most of which is
the runtime itself.

A native C++ program would be smaller still. It was passed over because the
whole thing was designed by iterating on its appearance, and a change-build-
look loop of a few seconds mattered more than the last thirty megabytes.

**Config as JSON, watched.** The file is the settings UI. It is rewritten
only when the user changes something through the widget (position, symbols,
city), and a parse error keeps the previous settings instead of overwriting
the file.

**Snapshot mode for testing.** `--snapshot <folder>` runs the real program
against the real services, renders the window to a PNG and exits, without
touching a running copy. Every visual change was checked this way, including
against deliberately awkward data: a city with rain all week, a city with a
forty-degree temperature spread, a light theme. `--frames <n>` adds animation
frames, which is how the animated image in the README was produced.

## Distribution

The default font is compiled into the exe, so the program is one file and can
be run from anywhere. Settings normally sit beside the exe, which keeps a
copied folder self-contained. Under Program Files that is not writable, so an
installed copy keeps them in `%APPDATA%\Pixill` instead; the rule is decided
by where the exe is, not by a switch.

The MSI installs for all users but writes the start-up entry for the
installing user only, in the same per-user place the tray menu's switch uses,
so the switch keeps working after an install. Installers ask a running program
to close by posting a close message to every one of its windows, including
hidden ones. Windows Forms gives a taskbar-less form a hidden owner window, and
closing that owner destroys the form without the normal close events; the
first installer test left the program running with no window for that reason.
Pixill now also exits when its window is destroyed from outside, and the
uninstall was re-tested with the program running.

## Known gaps

- The Show desktop path is essentially untested.
- Whether the visibility test is right on unusual setups (overlays that are
  neither transparent nor click-through, for instance) is not established.
- There is one widget on one display. Multiple instances are prevented.
- Menus and the search box use stock Windows controls and do not follow the
  pixel theme or dark mode.
- The stock source can disappear without notice. The client is isolated in
  one small class so another source could replace it.
