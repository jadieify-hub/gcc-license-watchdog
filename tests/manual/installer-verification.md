# Проверка установщика GCC License Watchdog

Дата проверки: 2026-08-31
ОС: Windows, локально установлен Guardant Control Center 4.0.5.3  
Целевая служба: `Guardant Control Center`  
REST API: `http://localhost:3189`

## Автоматизированная проверка

Запуск от имени администратора:

```powershell
.\tests\manual\run-installer-verification.ps1
```

Свежий установщик был установлен два раза подряд, после чего удалён через зарегистрированный Windows-деинсталлятор. Получен результат:

```json
{
  "InstallCycles": 2,
  "WatchdogServiceRemoved": true,
  "ConfigurationPreservedAcrossUpgrade": true,
  "ProgramDataAclHardened": true,
  "ProgramDataPreservedAfterSilentUninstall": true,
  "GuardantProcessIdBefore": "same as before installation",
  "GuardantProcessIdAfter": "same as before installation"
}
```

Проверено:

- повторная установка оставляет ровно одну запущенную службу `GCC License Watchdog`;
- конфигурация в `%ProgramData%` при обновлении не перезаписывается;
- каталог `%ProgramData%\GCC License Watchdog` не наследует ACL, запись разрешена только `SYSTEM` и администраторам, группе Users оставлено чтение и выполнение;
- запись в «Приложениях и возможностях» указывает на штатный `unins000.exe`;
- тихое удаление удаляет службу и каталог программы, сохраняя настройки и журналы;
- процесс `grdcontrol.exe` не перезапускался и сохранил исходный PID.

## Дополнительная ручная проверка

В отдельном установочном цикле подтверждены:

- учётная запись службы `LocalSystem`;
- режим запуска `Automatic (Delayed Start)`;
- восстановление Watchdog с задержками 5, 30 и 60 секунд;
- наличие ярлыка удаления в меню «Пуск»;
- работа Watchdog в течение более двух интервалов опроса при подключённом удалённом ключе без ложного перезапуска GCC;
- удаление через обычную запись Windows без изменения службы Guardant Control Center.

## Сборка

- версия: 0.1.2;
- тесты: 46 из 46 пройдены;
- размер установщика: 24 043 452 байта;
- SHA-256: `012AD8A7855BAF96637AFC168FA67C4BF795ED2643C4CE050988CEA68AE5678B`;
- Authenticode: `NotSigned` — для клиентской поставки рекомендуется подпись сертификатом издателя.

После проверки служба `GCC License Watchdog` удалена. `%ProgramData%` намеренно сохранён в соответствии с безопасным поведением тихого деинсталлятора; Guardant Control Center продолжает работать.
