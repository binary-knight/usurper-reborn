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

-- Window: game-themed custom title bar with integrated buttons
config.window_decorations = 'INTEGRATED_BUTTONS | RESIZE'
config.enable_tab_bar = true
config.use_fancy_tab_bar = true
config.hide_tab_bar_if_only_one_tab = false
config.tab_bar_at_bottom = false
config.enable_scroll_bar = false
config.window_close_confirmation = 'NeverPrompt'
config.initial_cols = 80
config.initial_rows = 50
config.window_padding = { left = 8, right = 8, top = 8, bottom = 8 }

-- Themed title bar frame — dark navy with gold accents
config.window_frame = {
    font = wezterm.font('JetBrains Mono', { weight = 'Bold' }),
    font_size = 11.0,
    -- Title bar background: deep dark navy
    active_titlebar_bg = '#0a0a1a',
    inactive_titlebar_bg = '#060610',
    active_titlebar_fg = '#c0a050',
    inactive_titlebar_fg = '#5a4a28',
    -- Gold accent line under the title bar
    active_titlebar_border_bottom = '#c0a050',
    inactive_titlebar_border_bottom = '#3a2a10',
    -- Window control buttons: gold on dark
    button_fg = '#c0a050',
    button_bg = '#0a0a1a',
    button_hover_fg = '#ffe0a0',
    button_hover_bg = '#2a1a0a',
    -- Thin gold border frame around the window
    border_left_width = '2px',
    border_right_width = '2px',
    border_bottom_height = '2px',
    border_top_height = '0px',
    border_left_color = '#c0a050',
    border_right_color = '#c0a050',
    border_bottom_color = '#c0a050',
    border_top_color = '#0a0a1a',
}

-- Style the single tab to look like a game title, not a browser tab
config.colors = {
    foreground = '#c0c0c0',
    background = '#000000',
    cursor_bg = '#c0a050',
    cursor_fg = '#000000',
    selection_bg = '#3a3a5a',
    selection_fg = '#ffffff',
    -- Tab bar styling (acts as our title bar)
    tab_bar = {
        background = '#0a0a1a',
        active_tab = {
            bg_color = '#0a0a1a',
            fg_color = '#c0a050',
            intensity = 'Bold',
        },
        inactive_tab = {
            bg_color = '#0a0a1a',
            fg_color = '#5a4a28',
        },
        inactive_tab_hover = {
            bg_color = '#1a1020',
            fg_color = '#c0a050',
        },
        new_tab = {
            bg_color = '#0a0a1a',
            fg_color = '#0a0a1a',
        },
        new_tab_hover = {
            bg_color = '#0a0a1a',
            fg_color = '#0a0a1a',
        },
    },
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

-- Custom tab title: game name in gold
wezterm.on('format-tab-title', function()
    return {
        { Attribute = { Intensity = 'Bold' } },
        { Foreground = { Color = '#c0a050' } },
        { Text = 'Usurper Reborn' },
    }
end)

-- Window title (for taskbar / Alt-Tab)
wezterm.on('format-window-title', function()
    return 'Usurper Reborn'
end)

-- Hide the new-tab button (single-app terminal)
config.show_new_tab_button_in_tab_bar = false

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

-- Center window on screen at startup
wezterm.on('gui-startup', function(cmd)
    local screen = wezterm.gui.screens().active
    -- Estimate window pixel size from cell dimensions + padding + title bar
    local cell_w = 8  -- approximate cell width at font_size 14
    local win_w = (config.initial_cols * cell_w) + config.window_padding.left + config.window_padding.right
    local x = (screen.width - win_w) / 2
    local y = 20  -- near top of screen so tall window doesn't clip taskbar
    local _, _, _ = wezterm.mux.spawn_window(cmd or {
        position = { x = x, y = y, origin = 'ActiveScreen' },
    })
end)

-- Disable unnecessary features for a game terminal
config.check_for_updates = false
config.show_update_window = false
config.audible_bell = 'Disabled'
config.warn_about_missing_glyphs = false

return config
