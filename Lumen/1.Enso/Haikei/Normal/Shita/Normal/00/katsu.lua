local base = 0
local light = 0
local light_counter = 0
local light_opacity = 153

function init()
    base = load_texture("Base.png")
    light = load_texture("Light.png")
end

function update(dt)
    light_counter = light_counter + 45.0 * dt
    if math.sin(light_counter) > 0 then
        light_opacity = 140
    else
        light_opacity = 153
    end
end

function draw()
    local s = math.min(state.width / 1920, state.height / 1080)
    local vx = (state.width - 1920 * s) / 2
    local vy = (state.height - 1080 * s) / 2
    local y = vy + 540 * s
    local w = 1920 * s
    local h = 540 * s

    if base ~= 0 then
        draw_texture(base, vx, y, w, h)
    end
    if light ~= 0 then
        begin_blend("add")
        draw_texture(light, vx, y, w, h, 0, 255, 255, 255, light_opacity)
        end_blend()
    end
end
