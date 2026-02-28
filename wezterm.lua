-- Usurper Reborn — WezTerm Game Terminal Configuration
-- This config turns WezTerm into a branded game window.
-- Launched via Play.bat (Windows) or play.sh (Linux/macOS).

local wezterm = require 'wezterm'
local config = wezterm.config_builder()

-- Resolve game executable relative to this config file's directory
-- NOTE: config_file_dir = directory containing THIS .lua file (the game folder)
--       config_dir = WezTerm's own config directory (wrong for bundled configs)
local config_dir = wezterm.config_file_dir or wezterm.config_dir
local is_windows = wezterm.target_triple:find('windows') ~= nil
local sep = is_windows and '\\' or '/'
local exe = config_dir .. sep .. (is_windows and 'UsurperReborn.exe' or 'UsurperReborn')
config.default_prog = { exe, '--local' }
config.default_cwd = config_dir

-- Window: clean game window, no terminal chrome
config.enable_tab_bar = false
config.enable_scroll_bar = false
config.window_decorations = 'RESIZE'
config.window_close_confirmation = 'NeverPrompt'
config.initial_cols = 80
config.initial_rows = 50
config.window_padding = { left = 8, right = 8, top = 8, bottom = 8 }

-- Close window on clean exit, hold open on crash so user can see the error
config.exit_behavior = 'CloseOnCleanExit'

-- Font: load bundled fonts from fonts/ directory, read player preference
config.font_dirs = { config_dir .. sep .. 'fonts' }
local font_name = 'JetBrains Mono'
local f = io.open(config_dir .. sep .. 'font-choice.txt', 'r')
if f then
    local line = f:read('*l')
    f:close()
    if line and #line > 0 then
        font_name = line
    end
end
config.font = wezterm.font(font_name)
config.font_size = 14.0
config.freetype_load_flags = 'NO_HINTING'

-- Color scheme: dark background, vivid standard ANSI colors for art fidelity
config.colors = {
    foreground = '#c0c0c0',
    background = '#000000',
    cursor_bg = '#c0a050',
    cursor_fg = '#000000',
    selection_bg = '#3a3a5a',
    selection_fg = '#ffffff',
    -- Standard ANSI colors — kept vivid so ANSI art renders correctly
    ansi = {
        '#000000',  -- black
        '#cc0000',  -- red
        '#00cc00',  -- green
        '#cccc00',  -- yellow
        '#0000cc',  -- blue
        '#cc00cc',  -- magenta
        '#00cccc',  -- cyan
        '#cccccc',  -- white
    },
    brights = {
        '#555555',  -- bright black (gray)
        '#ff5555',  -- bright red
        '#55ff55',  -- bright green
        '#ffff55',  -- bright yellow
        '#5555ff',  -- bright blue
        '#ff55ff',  -- bright magenta
        '#55ffff',  -- bright cyan
        '#ffffff',  -- bright white
    },
}

-- Window title
wezterm.on('format-window-title', function()
    return 'Usurper Reborn'
end)

-- Disable unnecessary features for a game terminal
config.check_for_updates = false
config.show_update_window = false
config.audible_bell = 'Disabled'
config.warn_about_missing_glyphs = false

return config
