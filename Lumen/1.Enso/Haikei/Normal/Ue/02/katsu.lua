local SCENE_W = 1920
local SCENE_H = 276

local normal_anims = {}
local normal_durs = {}
local clear_anims = {}
local clear_durs = {}

local base = 0
local prev_cleared = false

local function load_set(dir, anims, durs)
    local i = 0
    while true do
        local h = load_aup2(dir .. "/" .. i)
        if h == 0 then break end
        anims[#anims + 1] = h
        durs[#durs + 1] = aup2_duration(h)
        i = i + 1
    end
end

function init()
    load_set("Normal_1P", normal_anims, normal_durs)
    load_set("Clear", clear_anims, clear_durs)
end

function update(dt)
    if state.cleared and not prev_cleared then
        base = state.time
    end
    prev_cleared = state.cleared
end

local function draw_set(anims, durs, t0, x, y, w, h)
    for i = 1, #anims do
        if durs[i] > 0 then
            draw_aup2(anims[i], x, y, w, h, t0 % durs[i])
        end
    end
end

function draw()
    local s = math.min(state.width / 1920, state.height / 1080)
    local vx = (state.width - 1920 * s) / 2
    local vy = (state.height - 1080 * s) / 2
    local w = SCENE_W * s
    local h = SCENE_H * s

    if state.cleared then
        draw_set(clear_anims, clear_durs, state.time - base, vx, vy, w, h)
    else
        draw_set(normal_anims, normal_durs, state.time, vx, vy, w, h)
    end
end
