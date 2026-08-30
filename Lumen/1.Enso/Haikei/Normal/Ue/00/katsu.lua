local IMG      = "images/l4.png"
local IMG_C    = "images/4.png"
local IMG_W    = 496
local IMG_H    = 278
local SCROLL_S = 5.330
local LOTTIE   = "data.json"
local LOTTIE_C = "data_clear.json"
local LOTTIE_W = 490
local LOTTIE_H = 277
local LOTTIE_S = 2.660
local FADE_S   = 0.15

local img = 0
local imgC = 0
local anim = 0
local animC = 0
local offset = 0
local lottieOffset = 0
local clearFade = 0

function init()
    img = load_texture(IMG)
    imgC = load_texture(IMG_C)
    anim = load_lottie(LOTTIE)
    animC = load_lottie(LOTTIE_C)
    offset = 0
    lottieOffset = 0
    clearFade = 0
end

function update(dt)
    offset = (offset + (IMG_W / SCROLL_S) * dt) % IMG_W
    lottieOffset = (lottieOffset + (LOTTIE_W / LOTTIE_S) * dt) % LOTTIE_W

    local target = state.cleared and 1 or 0
    local step = (FADE_S > 0) and (dt / FADE_S) or 1
    if clearFade < target then
        clearFade = math.min(target, clearFade + step)
    elseif clearFade > target then
        clearFade = math.max(target, clearFade - step)
    end
end

function draw()
    local s = math.min(state.width / 1920, state.height / 1080)
    local vx = (state.width - 1920 * s) / 2
    local vy = (state.height - 1080 * s) / 2
    local a = math.floor(clearFade * 255 + 0.5)

    if img ~= 0 then
        local w = IMG_W * s
        local x = vx - offset * s
        while x < vx + 1920 * s do
            draw_texture(img, x, vy, w + 1, IMG_H * s)
            if a > 0 and imgC ~= 0 then
                draw_texture(imgC, x, vy, w + 1, IMG_H * s, 0, 255, 255, 255, a)
            end
            x = x + w
        end
    end

    if anim ~= 0 then
        local d = lottie_duration(anim)
        local t = (d > 0) and (state.time % d) or 0
        local w = LOTTIE_W * s
        local x = vx - lottieOffset * s
        while x < vx + 1920 * s do
            draw_lottie(anim, x, vy, w, LOTTIE_H * s, t)
            if a > 0 and animC ~= 0 then
                draw_lottie(animC, x, vy, w, LOTTIE_H * s, t, 0, 255, 255, 255, a)
            end
            x = x + w
        end
    end
end
