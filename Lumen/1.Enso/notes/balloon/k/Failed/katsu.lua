local anim = 0

function init()
    anim = load_lottie("data.json")
end

function draw()
    if anim == 0 then
        finish()
        return
    end

    local d = lottie_duration(anim)
    if state.time >= d then
        finish()
        return
    end

    local s = math.min(state.width / 1920, state.height / 1080)
    local vx = (state.width - 1920 * s) / 2
    local vy = (state.height - 1080 * s) / 2
    draw_lottie(anim, vx, vy + 231 * s, 1920 * s, 306 * s, state.time)
end
