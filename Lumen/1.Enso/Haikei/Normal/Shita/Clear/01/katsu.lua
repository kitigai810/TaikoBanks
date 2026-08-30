local SCENE_W = 1920
local SCENE_H = 540
local INTRO = 29 / 60

local anims = {}
local durs = {}

local base = 0
local prev_cleared = false

function init()
    local i = 0
    while true do
        local h = load_aup2(tostring(i))
        if h == 0 then break end
        anims[#anims + 1] = h
        durs[#durs + 1] = aup2_duration(h)
        i = i + 1
    end
end

function update(dt)
    if state.cleared and not prev_cleared then
        base = state.time
    end
    prev_cleared = state.cleared
end

function draw()
    local s = math.min(state.width / 1920, state.height / 1080)
    local vx = (state.width - 1920 * s) / 2
    local vy = (state.height - 1080 * s) / 2
    local x = vx
    local y = vy + (1080 - SCENE_H) * s
    local w = SCENE_W * s
    local h = SCENE_H * s

    local t0 = state.time - base
    for i = 1, #anims do
        local dur = durs[i]
        if dur > 0 then
            local t = t0
            if t >= INTRO and dur > INTRO then
                t = INTRO + (t - INTRO) % (dur - INTRO)
            elseif t >= dur then
                t = t % dur
            end
            draw_aup2(anims[i], x, y, w, h, t)
        end
    end
end
