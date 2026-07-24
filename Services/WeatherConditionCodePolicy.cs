namespace Task_Flyout.Services
{
    internal static class WeatherConditionCodePolicy
    {
        public static int WttrToOpenMeteo(int code) => code switch
        {
            143 or 248 or 260 => 45,
            200 or 386 or 389 or 392 or 395 => 95,
            281 or 284 or 311 or 314 or 350 or 362 or 365 or 374 or 377 => 66,
            302 or 308 or 359 => 65,
            176 or 263 or 266 or 293 or 296 or 299 or 305 or 356 => 61,
            227 or 335 or 338 or 371 => 75,
            179 or 182 or 185 or 323 or 326 or 329 or 332 or 368 => 71,
            _ => 0
        };
    }
}
