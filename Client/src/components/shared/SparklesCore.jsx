import { memo, useEffect, useId, useMemo, useState } from 'react';
import Particles, { initParticlesEngine } from '@tsparticles/react';
import { loadSlim } from '@tsparticles/slim';
import { motion, useAnimation } from 'framer-motion';

function SparklesCoreImpl({
    id,
    className = '',
    background = 'transparent',
    minSize = 0.6,
    maxSize = 1.6,
    speed = 1.2,
    particleColor = '#ffffff',
    particleDensity = 100,
}) {
    const [init, setInit] = useState(false);
    const controls = useAnimation();
    const generatedId = useId();

    useEffect(() => {
        initParticlesEngine(async (engine) => {
            await loadSlim(engine);
        }).then(() => setInit(true));
    }, []);

    const particlesLoaded = async (container) => {
        if (container) {
            controls.start({ opacity: 1, transition: { duration: 1 } });
        }
    };

    const options = useMemo(
        () => ({
            background: { color: { value: background } },
            fullScreen: { enable: false, zIndex: 1 },
            fpsLimit: 120,
            interactivity: {
                events: {
                    onClick: { enable: false, mode: 'push' },
                    onHover: { enable: false, mode: 'repulse' },
                    resize: true,
                },
            },
            particles: {
                color: { value: particleColor },
                move: {
                    enable: true,
                    direction: 'none',
                    outModes: { default: 'out' },
                    random: false,
                    speed: { min: 0.1, max: 1 },
                    straight: false,
                },
                number: {
                    density: { enable: true, width: 400, height: 400 },
                    value: particleDensity,
                },
                opacity: {
                    value: { min: 0.1, max: 1 },
                    animation: {
                        enable: true,
                        speed,
                        sync: false,
                        startValue: 'random',
                        mode: 'auto',
                    },
                },
                shape: { type: 'circle' },
                size: { value: { min: minSize, max: maxSize } },
            },
            detectRetina: true,
        }),
        [background, particleColor, particleDensity, speed, minSize, maxSize]
    );

    return (
        <motion.div animate={controls} initial={{ opacity: 0 }} className={className}>
            {init && (
                <Particles
                    id={id || generatedId}
                    className="h-full w-full"
                    particlesLoaded={particlesLoaded}
                    options={options}
                />
            )}
        </motion.div>
    );
}

export default memo(SparklesCoreImpl);
