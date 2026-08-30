local base = 0
local light = 0
local down = 0
local drop = 0

local light_counter = 0
local light_opacity = 140

local particles = {}
local num_particles = 7
local screen_width = 1920
local screen_height = 1080
local spawn_y = 506

local function reset_particle(i)
    local side = math.random()
    local angle_deg, speed

    if side < 0.5 then
        particles[i].x = math.random(-40, 198)
        angle_deg = math.random(20, 30)
    else
        particles[i].x = math.random(screen_width - 830, screen_width + 60)
        angle_deg = math.random(-30, -20)
    end

    particles[i].y = spawn_y
    speed = math.random(200, 300)

    local rad = math.rad(angle_deg)
    particles[i].speedX = speed * math.sin(rad)
    particles[i].speedY = speed * math.cos(rad)
end

function init()
    base = load_texture("1.png")
    light = load_texture("Light.png")
    down = load_texture("Down.png")
    drop = load_texture("2.png")

    for i = 1, num_particles do
        particles[i] = {}
        reset_particle(i)
        particles[i].y = math.random(spawn_y, screen_height)
    end
end

function update(dt)
    light_counter = light_counter + 60.0 * dt
    if math.sin(light_counter) > 0 then
        light_opacity = 128
    else
        light_opacity = 140
    end

    for i = 1, num_particles do
        particles[i].y = particles[i].y + particles[i].speedY * dt
        particles[i].x = particles[i].x + particles[i].speedX * dt

        if particles[i].y > screen_height or particles[i].x < -50 or particles[i].x > screen_width + 50 then
            reset_particle(i)
        end
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

    if down ~= 0 then
        draw_texture(down, vx, y, w, h)
    end

    if drop ~= 0 then
        local dw = tex_width(drop) * s
        local dh = tex_height(drop) * s
        for i = 1, num_particles do
            draw_texture(drop, vx + particles[i].x * s, vy + particles[i].y * s, dw, dh)
        end
    end
end
