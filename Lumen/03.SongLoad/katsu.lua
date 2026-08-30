local t = 0
local anim_in = 0
local anim_out = 0
local out_start = -1
local load_started = false

function init()
    t = 0
    out_start = -1
    load_started = false
    anim_in = load_lottie("in")
    anim_out = load_lottie("out")
end

function update(dt)
    t = t + dt

    local in_done = (anim_in == 0) or (t >= lottie_duration(anim_in))

    if in_done and not load_started then
        start_load()
        load_started = true
    end

    if state.loaded and in_done and out_start < 0 then
        out_start = t
    end

    if out_start >= 0 then
        if anim_out ~= 0 then
            if (t - out_start) >= lottie_duration(anim_out) then finish() end
        else
            finish()
        end
    end
end

function draw()
    local w, h = state.width, state.height

    if out_start >= 0 and anim_out ~= 0 then
        local od = lottie_duration(anim_out)
        draw_lottie(anim_out, 0, 0, w, h, math.min(t - out_start, od - 0.0001))
    elseif anim_in ~= 0 then
        local id = lottie_duration(anim_in)
        draw_lottie(anim_in, 0, 0, w, h, math.min(t, id - 0.0001))
    else
        local dots = string.rep(".", math.floor(t * 2) % 4)
        draw_text("Now Loading" .. dots, w * 0.5 - 130, h * 0.5, 40, 200, 200, 200, 255)
    end
end
