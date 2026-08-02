document.addEventListener('DOMContentLoaded', function () {

    // ===== HERO SLIDER =====
    const slides = document.querySelectorAll('.hero-slide');
    const dots = document.querySelectorAll('.hero-slider-dots .dot');
    if (slides.length > 1) {
        let current = 0;
        function goToSlide(idx) {
            slides[current].classList.remove('active');
            dots[current]?.classList.remove('active');
            current = idx;
            slides[current].classList.add('active');
            dots[current]?.classList.add('active');
        }
        setInterval(() => goToSlide((current + 1) % slides.length), 5000);
        dots.forEach(dot => {
            dot.addEventListener('click', () => goToSlide(parseInt(dot.dataset.slide)));
        });
    }

    // ===== ANIMATED TEXTS =====
    const textEl = document.querySelector('.animated-text-rotate');
    if (textEl) {
        const texts = JSON.parse(textEl.dataset.texts || '[]');
        if (texts.length > 1) {
            let idx = 0;
            setInterval(() => {
                textEl.style.opacity = '0';
                textEl.style.transform = 'translateY(-10px)';
                setTimeout(() => {
                    idx = (idx + 1) % texts.length;
                    textEl.textContent = texts[idx];
                    textEl.style.opacity = '1';
                    textEl.style.transform = 'translateY(0)';
                }, 400);
            }, 3000);
            textEl.style.transition = 'opacity 0.4s ease, transform 0.4s ease';
        }
    }

    // ===== ANIMATED COUNTERS =====
    const counters = document.querySelectorAll('.counter');
    if (counters.length) {
        const counterObserver = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const el = entry.target;
                    const target = parseInt(el.dataset.target) || 0;
                    let count = 0;
                    const duration = 1500;
                    const step = Math.ceil(target / (duration / 16));
                    const timer = setInterval(() => {
                        count += step;
                        if (count >= target) {
                            count = target;
                            clearInterval(timer);
                        }
                        el.textContent = count;
                    }, 16);
                    counterObserver.unobserve(el);
                }
            });
        }, { threshold: 0.3 });
        counters.forEach(c => counterObserver.observe(c));
    }

    // ===== SCROLL ANIMATIONS =====
    const fadeObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('visible');
            }
        });
    }, { threshold: 0.1, rootMargin: '0px 0px -50px 0px' });
    document.querySelectorAll('.fade-up').forEach(el => fadeObserver.observe(el));

    // ===== SMOOTH SCROLL =====
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                e.preventDefault();
                target.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    });
});
