local img = 0
local anim = 0
local dur = 0
local base = 0
local prev_cleared = false

function init()
    img = load_texture("0.png")
    anim = load_aup2("Anime.aup2")
    if anim ~= 0 then
        dur = aup2_duration(anim)
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

    if anim ~= 0 and dur > 0 then
        local intro = 30 / 60
        local t = state.time - base
        if t >= intro then
            t = intro + (t - intro) % (dur - intro)
        end
        draw_aup2(anim, vx, vy + 540 * s, 1920 * s, 540 * s, t)
    elseif img ~= 0 then
        draw_texture(img, vx, vy + 540 * s, 1920 * s, 540 * s)
    end
end
